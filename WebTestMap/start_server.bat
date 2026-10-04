@echo off
rem Запуск тестовой карты в браузере (нужен Python 3). Сервер нужен, т.к. браузер не грузит модели с file://
cd /d "%~dp0\.."
start "" http://localhost:8000/WebTestMap/
python -m http.server 8000
