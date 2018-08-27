cd %~dp0

start Server/bin/Release/server.exe

timeout 1

start Proxy/bin/Release/proxy.exe

timeout 1

start Client/bin/Release/client.exe