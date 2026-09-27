# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN apt-get update \
    && apt-get install -y --no-install-recommends clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY . .
RUN dotnet publish src/Lapse.Cli -c Release -r linux-x64 -o /app \
    && mkdir /data

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
COPY --from=build /app/lapse /app/libe_sqlite3.so /app/
COPY --from=build --chown=app:app /data /data
WORKDIR /data
USER app
VOLUME /data
ENTRYPOINT ["/app/lapse"]
CMD ["watch", "--every", "6h"]
