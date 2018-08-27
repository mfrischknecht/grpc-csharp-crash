cd %~dp0
set TOOLS=%~dp0..\packages\Grpc.Tools.1.14.1\tools\windows_x64
set WKT=%~dp0..\packages\Google.Protobuf.Tools.3.6.1\tools
"%TOOLS%\protoc.exe" "--proto_path=." "-I=%WKT%" --csharp_out "%~dp0src" --grpc_out "%~dp0src" "--plugin=protoc-gen-grpc=%TOOLS%\grpc_csharp_plugin.exe" "protocol.proto" 

@REM echo Error code: %errorlevel%