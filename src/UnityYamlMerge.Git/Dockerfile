FROM docker:28-cli@sha256:625d9431a9f54c5a2bc90f24f0e1c3d55b1349fd857dd85035f98c2c9acbdd4d AS base
RUN apk add --no-cache git openssl libssl3
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:940f919ae84dd92ccd4aab7686fa5b777870b006c9360351039e16bcaad73d89 AS publish
RUN apk add --no-cache clang build-base zlib-dev file
ARG BUILD_CONFIGURATION=Release
ARG BUILD_OUTPUT_DIR=/app/publish
WORKDIR /src
COPY ["src/UnityYamlMerge.Core/UnityYamlMerge.Core.csproj", "src/UnityYamlMerge.Core/"]
COPY ["src/UnityYamlMerge.Git/UnityYamlMerge.Git.csproj", "src/UnityYamlMerge.Git/"]
RUN dotnet restore "src/UnityYamlMerge.Git/UnityYamlMerge.Git.csproj"
COPY . .
WORKDIR "/src/src/UnityYamlMerge.Git"
RUN dotnet publish "./UnityYamlMerge.Git.csproj" -c $BUILD_CONFIGURATION -o $BUILD_OUTPUT_DIR

# ---- Verification: Cause the build to fail if not statically linked ----
RUN ls -R $BUILD_OUTPUT_DIR
RUN set -eux; \
    file $BUILD_OUTPUT_DIR/UnityYamlMerge.Git | tee /tmp/file.txt; \
    if readelf -l $BUILD_OUTPUT_DIR/UnityYamlMerge.Git | grep -q 'INTERP'; then \
        echo "ERROR: dynamic loader detected"; exit 1; \
    fi

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["/app/UnityYamlMerge.Git"]