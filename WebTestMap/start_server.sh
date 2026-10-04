#!/bin/sh
# Запуск тестовой карты в браузере (нужен Python 3)
cd "$(dirname "$0")/.." || exit 1
echo "Откройте http://localhost:8000/WebTestMap/"
python3 -m http.server 8000
