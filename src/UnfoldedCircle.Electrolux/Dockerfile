FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 9001

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY [".editorconfig", "."]
COPY ["src/UnfoldedCircle.Electrolux/UnfoldedCircle.Electrolux.csproj", "UnfoldedCircle.Electrolux/"]
RUN dotnet restore "UnfoldedCircle.Electrolux/UnfoldedCircle.Electrolux.csproj"
COPY ./src .

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR "/src/UnfoldedCircle.Electrolux"
RUN dotnet publish "UnfoldedCircle.Electrolux.csproj" -c "$BUILD_CONFIGURATION" -o /app/publish /p:PublishTrimmed=false /p:PublishSingleFile=true /p:UseAppHost=true
RUN touch /app/publish/configured_entities.json /app/publish/token.json && \
    chown "$APP_UID" /app/publish/configured_entities.json /app/publish/token.json && \
    chmod 644 /app/publish/configured_entities.json /app/publish/token.json

FROM base AS final
USER $APP_UID
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["./driver"]