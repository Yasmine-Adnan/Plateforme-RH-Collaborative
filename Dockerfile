# ================= Étape 1 : BUILD  =================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restaurer les dépendances (mis en cache tant que le .csproj ne change pas)
COPY PlateformeRHCollaborative.Web.csproj ./
RUN dotnet restore PlateformeRHCollaborative.Web.csproj

# Copier le code et publier l'application
COPY . .
RUN dotnet publish PlateformeRHCollaborative.Web.csproj -c Release -o /app/publish --no-restore

# ================= Étape 2 : RUNTIME  =================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PlateformeRHCollaborative.Web.dll"]