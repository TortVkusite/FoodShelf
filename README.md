# FoodShelf

Учёт сроков годности продуктов. Автор: **Однобурцев Никата Максимович**, О-23-ИВТ-2-по-Б. Основная тема сохранена; дополнительный модуль клиентов и заказов закрывает предметную область общих заданий Backend и Frontend.

Публичный репозиторий: https://github.com/TortVkusite/FoodShelf

## Запуск для демонстрации

В комплекте сдачи есть автономная Windows x64 версия. Распакуйте её полностью, запустите **Запустить.cmd** и откройте http://localhost:5088. Устанавливать .NET не требуется. Данные SQLite сохраняются рядом с программой. Окно программы должно оставаться открытым; остановка — Ctrl+C.

Из исходников нужен .NET SDK 9 или 10:

```powershell
dotnet run --project src/FoodShelf.Api -- --Demo=true --urls=http://localhost:5088
```

В `wwwroot` уже включена готовая сборка Angular. Swagger находится на http://localhost:5088/swagger. При первом запуске демо создаёт продукты, категории и места хранения. Заказы и клиенты в API сначала пустые; в разделе «Заказы» есть отдельный JSON-источник с примерами. Браузерные JSON-примеры не являются записями PostgreSQL.

## Основной запуск PostgreSQL и Nginx

Остановите демо, включите Docker Desktop и из папки решения выполните:

```powershell
docker compose up --build -d
```

Адрес: http://localhost:5088. Состав: Nginx → ASP.NET Core → PostgreSQL 17. База сначала пустая: добавьте категорию и место, затем продукт. Данные хранятся в Docker volume. `docker compose down` останавливает сервисы, сохраняя данные. Локальный учебный пароль БД — foodshelf_local; его можно заменить переменной POSTGRES_PASSWORD в `.env`.

Для запуска API без контейнера, но с PostgreSQL:

```powershell
docker compose up db -d
dotnet run --project src/FoodShelf.Api -- --urls=http://localhost:5088
```

По умолчанию используется localhost:5434, база/пользователь foodshelf. Другую строку задаёт `ConnectionStrings__Default`. Настройка GitLab CE, Runner и Nginx описана в **deploy/README.md**.

## Возможности

- CRUD и получение по ID для продуктов, категорий, мест хранения, клиентов и заказов.
- Поиск русских названий без учёта регистра, фильтры по полям, датам и числовым диапазонам, сортировка и страницы.
- Дата годности включительна: вчера — expired, сегодня и следующие три дня — soon, далее — fresh. Расчёт по московской дате.
- Связанные категории, места и клиенты защищены от удаления. Количество продукта положительное, до трёх дробных знаков; сумма заказа — до двух.
- Angular 22.2.6, сборщик Angular CLI и SCSS. Индикатор загрузки, формы с валидацией, сортировка по заголовкам и адаптивная таблица.
- Latest Orders: AJAX из MOCK_DATA.json, счётчик, New Entry, редактирование по строке/карандашу, некликабельный Archive, переключаемые Email/Address. JSON-изменения сохраняются в localStorage браузера; переключатель «REST API / база данных» работает с настоящими заказами.
- Четыре хранимые процедуры PostgreSQL: истекающие продукты, сводка по категориям, сумма выполненных заказов в день рождения и средний чек по 24 часам. Refcursor читается внутри транзакции. В демо используется эквивалентная логика C#.

## Сборка клиента

Нужен Node.js **24.15.0 или новее в ветке 24**, либо совместимая с Angular 22 версия из официальной таблицы. `package-lock.json` фиксирует зависимости.

```powershell
cd frontend
npm ci
npm run build
```

Результат: `frontend/dist/foodshelf-web/browser`. Скопируйте его содержимое в `src/FoodShelf.Api/wwwroot`, заменив предыдущую сборку. Dockerfile выполняет сборку и копирование автоматически. `frontend/proxy.conf.json` направляет `/api` на локальный сервер для `npm start`.

## Проверки

```powershell
dotnet test FoodShelf.sln -c Release
./scripts/SmokeTest.ps1
./scripts/VerifyReports.ps1
```

Последние два скрипта используют запущенное приложение; для настоящих SQL-процедур запускайте PostgreSQL-режим. Можно передать `-BaseUrl http://localhost:PORT`. Они создают собственные тестовые записи и затем удаляют их.

Браузерный тест требует `npm ci` в frontend и установленный браузер Playwright:

```powershell
cd frontend
npx playwright install chromium
cd ..
node scripts/ui-smoke.mjs
```

Для установленного Chrome или Edge вместо скачивания задайте `PLAYWRIGHT_EXECUTABLE_PATH` абсолютным путём к браузеру. `E2E_BASE_URL` задаёт адрес приложения. Тест запускает отдельный браузерный контекст и не затрагивает сохранённые JSON-примеры пользователя.

## Структура

| Путь | Содержимое |
|---|---|
| src/FoodShelf.Api/Program.cs | Маршруты продуктов, справочников, статистики |
| src/FoodShelf.Api/Models.cs | Модели основной темы, EF и правила сроков |
| src/FoodShelf.Api/Orders.cs | Учебный модуль клиентов, заказов и отчётов |
| src/FoodShelf.Api/Sql/procedures.sql | Процедуры аналитики и сводки; процедура сроков создаётся в Program.cs |
| frontend/src/app/shelf.ts, shelf.html | Angular-компонент интерфейса |
| frontend/src/styles.scss, extra.scss | SCSS-стили |
| tests/FoodShelf.Tests | 35 unit- и API-проверок |
| deploy | Nginx, GitLab CE и инструкция Runner |
| .gitlab-ci.yml | Автоматические build → test → deploy |
| scripts | Проверки API, процедур и браузерного сценария |

Схема создаётся при первом запуске. Для существующей ранней базы FoodShelf дополнительные таблицы клиентов/заказов создаются без удаления продуктов. Дальнейшие изменения схемы следует сопровождать EF migrations. Для небольшого учебного набора данных фильтры клиентов/заказов по тексту и части диапазонов выполняются после чтения EF; при росте объёма их следует перенести в SQL и использовать нормализованные столбцы.

## Что ещё подтвердить перед сдачей

Работа приложения, 35 тестов, PostgreSQL и нативный Nginx проверены. Сборка Docker и запуск Compose через Nginx проверены в [GitHub Actions](https://github.com/TortVkusite/FoodShelf/actions/runs/43286514766). Из-за недоступности Docker daemon фактические GitLab CE и pipeline пока не запускались. Публичный GitHub-репозиторий опубликован: https://github.com/TortVkusite/FoodShelf . Проверки GitHub Actions запускаются автоматически при push. Точное состояние требований — в **Соответствие_требованиям.md**. Подписи, оценки и отметки руководителя оставлены пустыми; даты дневника являются календарным планом.

Документация: [Angular compatibility](https://angular.dev/reference/versions), [GitLab Docker](https://docs.gitlab.com/install/docker/), [Nginx](https://nginx.org/en/docs/).
