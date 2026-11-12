FROM node:24.15.0-alpine AS frontend
WORKDIR /web
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/FoodShelf.Api/FoodShelf.Api.csproj src/FoodShelf.Api/
RUN dotnet restore src/FoodShelf.Api/FoodShelf.Api.csproj
COPY src/ src/
COPY --from=frontend /web/dist/foodshelf-web/browser/ src/FoodShelf.Api/wwwroot/
RUN dotnet publish src/FoodShelf.Api/FoodShelf.Api.csproj -c Release --no-restore -o /app
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FoodShelf.Api.dll"]
