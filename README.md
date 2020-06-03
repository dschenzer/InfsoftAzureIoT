# InfsoftAzureIoT

Infsoft Azure IoT (Real-Time Locating Systems, RTLS)  

Calls the Infsoft REST API to obtain assets.
You need an API key in order to consume the API.  

In the 'InfsoftAPI' folder you can find a library to consume all the Rest API which are currently exposed in v1 (<https://api.infsoft.com/console//index#/).>

# Build IoT Edge container

    docker build --rm -f "C:\Code\infsoft\Infsoft_Rest\Dockerfile.windows-amd64" -t foxacr.azurecr.io/foxassettracking:0.0.1-windows-amd64 "C:\Code\infsoft\Infsoft_Rest"   
