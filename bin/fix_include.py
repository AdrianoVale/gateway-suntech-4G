#!/usr/bin/env python3
files = [
    '/var/www/html/app/cabecalho.php',
    '/var/www/html/app-mobile/cabecalho.php',
]
old = 'include_once "./funcoesBanco/jwt_helper.php";'
new = 'include_once __DIR__ . "/funcoesBanco/jwt_helper.php";'
for f in files:
    txt = open(f, 'r').read()
    txt2 = txt.replace(old, new)
    if txt2 != txt:
        open(f, 'w').write(txt2)
        print('Fixed: ' + f)
    else:
        print('No match: ' + f)
