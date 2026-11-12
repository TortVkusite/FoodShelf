# Развёртывание и GitLab CE

## Приложение и Nginx

В корне проекта выполните `docker compose up --build -d`. Откройте http://localhost:5088. Запросы принимает Nginx, затем передаёт ASP.NET Core; база находится в отдельном контейнере. Docker volume хранит данные между запусками. Проверка: `docker compose ps`, `curl http://localhost:5088/api/health`, `./scripts/VerifyReports.ps1`.

Для локальной проверки Windows без контейнеров: запустите приложение на 5088, скачайте Nginx с официального сайта, замените `conf/nginx.conf` содержимым `nginx.windows.conf`, выполните `nginx -t`, затем `nginx`. Прокси доступен по http://localhost:8088. Остановка: `nginx -s quit` из папки Nginx.

## GitLab CE

Нужен работающий Docker Engine с Linux-контейнерами. GitLab и Runner запускаются отдельно от приложения, чтобы не занимать память при обычной демонстрации:

```powershell
docker compose -f deploy/gitlab.compose.yaml up -d
docker compose -f deploy/gitlab.compose.yaml logs -f gitlab
```

GitLab может запускаться несколько минут. Откройте http://localhost:8929. Первоначальный пароль администратора выводится командой ниже; не включайте его в отчёт или репозиторий:

```powershell
docker compose -f deploy/gitlab.compose.yaml exec gitlab grep 'Password:' /etc/gitlab/initial_root_password
```

Войдите как root и измените пароль. Создайте проект `FoodShelf`. В настройках CI/CD → Runners создайте Project runner с тегом `foodshelf`, получите токен `glrt-…`. Зарегистрируйте Runner, заменив TOKEN:

```powershell
docker compose -f deploy/gitlab.compose.yaml exec runner gitlab-runner register --non-interactive --url http://host.docker.internal:8929 --token TOKEN --executor docker --docker-image docker:cli --docker-volumes /var/run/docker.sock:/var/run/docker.sock --clone-url http://host.docker.internal:8929 --description FoodShelf
```

Для Linux добавьте `extra_hosts: ["host.docker.internal:host-gateway"]` сервису Runner и `extra_hosts = ["host.docker.internal:host-gateway"]` в секцию `[runners.docker]` его `/etc/gitlab-runner/config.toml`. В Docker Desktop имя host.docker.internal встроено. Если checkout не разрешает имя gitlab.local, добавьте `clone_url = "http://host.docker.internal:8929"` в секцию `[[runners]]`. Доступ к Docker socket нужен для сборки и локального развёртывания на том же учебном компьютере; используйте отдельный доверенный Runner для этого проекта.

Добавьте GitLab remote и отправьте ветку main, используя URL своего проекта из интерфейса GitLab:

```powershell
git remote add gitlab http://localhost:8929/root/FoodShelf.git
git push gitlab main
```

Pipeline из `.gitlab-ci.yml` автоматически выполняет **build → test → deploy**. На main разворачивается Compose-проект foodshelf. Перед первым deployment остановите демо на порту 5088. Job deploy проверяет доступность Nginx и БД через `/api/health`. Пароль БД задавайте CI/CD-переменной POSTGRES_PASSWORD, если меняли значение по умолчанию.

Для отчёта сделайте реальные снимки: GitLab CE, online Runner, зелёные jobs build/test/deploy, `docker compose ps`, приложение через Nginx. Наличие конфигурационных файлов само по себе не подтверждает запуск pipeline.

## Публичный GitHub

В архив не входят учётные данные. Через GitHub создайте пустой публичный репозиторий FoodShelf. Из корня исходников:

```powershell
git init -b main
git add .
git commit -m "FoodShelf practice project"
git remote add origin https://github.com/YOUR_LOGIN/FoodShelf.git
git push -u origin main
```

Перед `git add` проверьте `.gitignore`: база демо, `.env`, журналы, node_modules и каталоги сборки исключены. В отчёт впишите фактический адрес репозитория после публикации. Если есть GitHub CLI, вместо создания через сайт можно выполнить `gh auth login`, затем `gh repo create FoodShelf --public --source=. --remote=origin --push` после первого коммита.

Официальные материалы: [GitLab Docker](https://docs.gitlab.com/install/docker/), [Runner Docker](https://docs.gitlab.com/runner/install/docker/), [Nginx](https://nginx.org/en/docs/).
