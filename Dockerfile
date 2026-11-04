FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/WorkTimeTracker/WorkTimeTracker.csproj src/WorkTimeTracker/
RUN dotnet restore src/WorkTimeTracker/WorkTimeTracker.csproj

COPY src/WorkTimeTracker/ src/WorkTimeTracker/
RUN dotnet publish src/WorkTimeTracker/WorkTimeTracker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

COPY --from=build /app/publish .
RUN mkdir -p /app/App_Data && chown -R app:app /app

USER app
VOLUME ["/app/App_Data"]
EXPOSE 8080
ENTRYPOINT ["dotnet", "WorkTimeTracker.dll"]
