FROM mcr.microsoft.com/dotnet/runtime:8.0
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false TZ=Asia/Shanghai
WORKDIR /opt/classic
COPY host/ host/
COPY services/ services/
COPY content-manifest.json content-manifest.json
RUN test -s services/GameSrv/Map/0.map || (echo "Import matching map resources before Docker build"; exit 1)
VOLUME /state
ENTRYPOINT ["dotnet", "/opt/classic/host/ClassicHost.dll"]
