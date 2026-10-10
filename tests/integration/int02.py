#!/usr/bin/env python3
"""INT-02: venta confirmada por API -> inventario -> catálogo, sin Stripe."""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import secrets
import subprocess
import time
import urllib.request
import urllib.error
import uuid

COMPOSE_FILE = os.getenv("INTEGRATION_COMPOSE_FILE")


def compose_command():
    """Devuelve el comando Compose del entorno actual, local o CI."""
    project_directory = Path(os.getenv("INTEGRATION_COMPOSE_DIRECTORY", Path.cwd())).resolve()
    command = ["docker", "compose", "--project-directory", str(project_directory)]
    if COMPOSE_FILE:
        compose_file = Path(COMPOSE_FILE)
        if not compose_file.is_absolute():
            compose_file = project_directory / compose_file
        command.extend(["-f", str(compose_file)])
    return command


def check(condition, message):
    if not condition:
        raise AssertionError(message)


class Test:
    def __init__(self, base, report):
        self.base, self.report = base.rstrip('/'), report
        self.tenant = None

    def request(self, method, path, body=None, token=None, timeout=15):
        headers = {'Accept': 'application/json'}
        if self.tenant:
            headers['X-Tenant-ID'] = self.tenant
        if token:
            headers['Authorization'] = 'Bearer ' + token
        data = None
        if body is not None:
            data = json.dumps(body).encode()
            headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(self.base + path, data=data, headers=headers, method=method)
        start = time.perf_counter()
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                payload = json.load(response)
                status = response.status
        except urllib.error.HTTPError as error:
            self.report['http'].append({'method': method, 'path': path, 'status': error.code})
            raise RuntimeError(f'{method} {path}: HTTP {error.code}') from None
        self.report['http'].append({'method': method, 'path': path, 'status': status,
                                   'duration_ms': round((time.perf_counter() - start) * 1000, 3)})
        return payload

    def execute(self):
        tag = 'int02-' + uuid.uuid4().hex[:12]
        password = secrets.token_urlsafe(24) + 'aA1!'
        admin_email = tag + '-admin@example.com'
        admin = self.request('POST', '/api/v1/auth/register', {
            'correo': admin_email, 'nombre': tag, 'contrasena': password,
            'tipoUsuario': 'administrador', 'rol': 'administrador',
            'direccion': 'INT-02 local', 'telefono': '+50200000000'})
        token = admin.get('token') or admin.get('accessToken')
        check(token, 'Registro administrador sin token')
        stores = self.request('GET', '/api/v1/tiendas', token=token)
        check(len(stores) == 1, 'Se esperaba exactamente una tienda propia para el usuario nuevo')
        self.tenant = stores[0]['id']
        self.report['tenant_id'] = self.tenant
        # Refrescar claims después de la creación de tienda, igual que SEG-01.
        admin = self.request('POST', '/api/v1/auth/login', {'correo': admin_email, 'contrasena': password})
        token = admin.get('token') or admin.get('accessToken')
        check(token, 'Login administrador sin token')
        customer = self.request('POST', '/api/v1/auth/register', {
            'correo': tag + '-cliente@example.com', 'nombre': tag + ' cliente',
            'contrasena': password, 'tipoUsuario': 'cliente', 'tipoCliente': 'particular'})
        customer_token = customer.get('token') or customer.get('accessToken')
        check(customer_token, 'Registro cliente sin token')
        branches = self.request('GET', '/api/v1/sucursales', token=token)
        check(len(branches) == 1, 'Se esperaba una sucursal en la tienda de prueba')
        branch = branches[0]['id']
        product = self.request('POST', '/api/v1/productos', {
            'nombre': tag, 'sku': tag, 'precioMayoreo': 10, 'precioDetalle': 15,
            'publicado': True, 'stockMinimo': 0,
            'stockSucursales': [{'sucursalId': branch, 'stock': 10}]}, token)
        pid = product['id']
        self.report.update(product_id=pid, branch_id=branch, product_name=tag)
        path = '/api/v1/productos/' + pid
        before = self.request('GET', path)
        check(before['stockTotal'] == 10, 'Stock inicial del catálogo distinto de 10')
        self.request('POST', '/api/v1/carrito/articulos', {'productoId': pid, 'cantidad': 3}, customer_token)
        reservation = self.request('POST', '/api/v1/reservaciones', {'sucursalId': branch}, customer_token)
        rid = reservation['id']
        self.report['reservation_id'] = rid
        check(reservation['estadoPago'] == 'pendiente', 'Reservación inicial no pendiente')
        check(self.request('GET', path)['stockTotal'] == 10, 'La reserva pendiente descontó stock')
        paid = self.request('PATCH', '/api/v1/reservaciones/' + rid + '/estado', {'estadoPago': 'pagado'}, token)
        confirmed = time.perf_counter()
        check(paid['estadoPago'] == 'pagado', 'No se confirmó el pago manual')
        observations = []
        while True:
            remaining = 2 - (time.perf_counter() - confirmed)
            check(remaining > 0, 'El catálogo no reflejó 7 unidades en <= 2 s')
            current = self.request('GET', path, timeout=remaining)
            elapsed = time.perf_counter() - confirmed
            observations.append({'elapsed_ms': round(elapsed * 1000, 3), 'stock': current['stockTotal']})
            self.report['observations'] = observations
            if current['stockTotal'] == 7:
                check(elapsed <= 2, 'El catálogo respondió fuera del límite de 2 s')
                break
            time.sleep(min(.1, max(0, 2 - elapsed)))
        self.report['catalog_delay_ms'] = observations[-1]['elapsed_ms']
        listing = self.request('GET', '/api/v1/productos')
        matches = [p for p in listing if p['id'] == pid]
        check(len(matches) == 1 and matches[0]['stockTotal'] == 7, 'Listado de catálogo inconsistente')
        inventory = self.request('GET', '/api/v1/inventarios/sucursal/' + branch, token=token)
        stocks = [i for i in inventory if i['productoId'] == pid]
        check(len(stocks) == 1 and stocks[0]['stock'] == 7, 'Inventario de sucursal distinto de 7')
        purchases = self.request('GET', '/api/v1/reservaciones/mis-compras', token=customer_token)
        check(len(purchases) == 1 and purchases[0]['id'] == rid, 'Compra duplicada o ausente')
        details = purchases[0]['detalles']
        check(purchases[0]['estadoPago'] == 'pagado' and len(details) == 1
              and details[0]['productoId'] == pid and details[0]['cantidad'] == 3, 'Detalle o estado persistido incorrecto')
        # Reenviar la misma confirmación no debe descontar otras tres unidades.
        self.request('PATCH', '/api/v1/reservaciones/' + rid + '/estado', {'estadoPago': 'pagado'}, token)
        check(self.request('GET', path)['stockTotal'] == 7, 'Descuento duplicado al repetir confirmación')
        # Verificación independiente y solo de lectura contra PostgreSQL de Compose.
        q = lambda value: "'" + value.replace("'", "''") + "'"
        query = f'''SELECT json_build_object(
          'stock_global', (SELECT stock_actual FROM "Producto" WHERE id={q(pid)}),
          'stock_sucursal', (SELECT stock FROM "Inventario" WHERE producto_id={q(pid)} AND sucursal_id={q(branch)}),
          'ventas', (SELECT count(*) FROM "Reservacion" WHERE tienda_id={q(self.tenant)}),
          'pagadas', (SELECT count(*) FROM "Reservacion" WHERE id={q(rid)} AND estado_pago='pagado'),
          'detalles', (SELECT count(*) FROM "DetalleReservacion" WHERE reservacion_id={q(rid)}),
          'unidades', (SELECT sum(cantidad) FROM "DetalleReservacion" WHERE reservacion_id={q(rid)}));'''
        command = compose_command() + ['exec', '-T', 'db',
                                       'sh', '-ec', 'exec psql -X -v ON_ERROR_STOP=1 -At -U "$POSTGRES_USER" -d "$POSTGRES_DB"']
        result = subprocess.run(command, input=query.encode(), stdout=subprocess.PIPE, check=True)
        persisted = json.loads(result.stdout)
        self.report['database'] = persisted
        check(persisted == {'stock_global': 7, 'stock_sucursal': 7, 'ventas': 1, 'pagadas': 1,
                            'detalles': 1, 'unidades': 3}, 'La base no coincide con la venta esperada')
        self.report.update(status='PASSED', initial_stock=10, sold=3, final_stock=7,
                           repeated_confirmation='No additional discount')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base-url', default='http://localhost:5000')
    args = parser.parse_args()
    check(args.base_url.rstrip('/') in ('http://localhost:5000', 'http://127.0.0.1:5000'),
          'Esta prueba verifica la BD local de Compose; usar la API local en puerto 5000')
    os.umask(0o077)
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    folder = Path(__file__).parent / 'artifacts' / stamp
    folder.mkdir(parents=True)
    report = {'case': 'INT-02', 'started_utc': stamp, 'status': 'FAILED', 'http': [],
              'base_url': args.base_url, 'threshold_ms': 2000, 'poll_interval_ms': 100,
              'cleanup': 'Test tenant and records retained for inspection; no existing business data changed'}
    try:
        Test(args.base_url, report).execute()
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        (folder / 'result.json').write_text(json.dumps(report, indent=2))
        print(f"INT-02: {report['status']}; evidencia: {folder / 'result.json'}", flush=True)
        if report['status'] == 'PASSED':
            print(f"Stock 10 -> 7; catálogo: {report['catalog_delay_ms']} ms")


if __name__ == '__main__':
    main()
