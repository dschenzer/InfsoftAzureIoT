namespace FoxAssetTracking
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using InfsoftRest;
    using InfsoftRest.Models;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;

    class Program
    {
        static int counter;
        static CancellationTokenSource cts;
        static InfsoftRest.InfsoftAPI api;

        static void Main(string[] args)
        {
            cts = new CancellationTokenSource();

            Init().Wait();

            // Wait until the app unloads or is cancelled            
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init()
        {
            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            ModuleClient ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            await ioTHubModuleClient.OpenAsync();
            Console.WriteLine("IoT Hub module client initialized.");

            api = new InfsoftRest.InfsoftAPI();

            // Register callback to be called when a message is received by the module
            //await ioTHubModuleClient.SetInputMessageHandlerAsync("input1", PipeMessage, ioTHubModuleClient);

            ioTHubModuleClient.SetConnectionStatusChangesHandler(ConnectionStatusHandler);

            Guid apiKey = Guid.Empty;
            var apiKeyEnv = Environment.GetEnvironmentVariable("apikey");

            if (!string.IsNullOrEmpty(apiKeyEnv))
            {
                apiKey = Guid.Parse(apiKeyEnv);
            }

            int locationId = 0;
            var locationIdEnv = Environment.GetEnvironmentVariable("locationid");

            if (!string.IsNullOrEmpty(locationIdEnv))
            {
                locationId = int.Parse(locationIdEnv);
            }

            int sleepDurationoInSec = 0;
            var sleepDurationoInSecEnv = Environment.GetEnvironmentVariable("sleepdurationoinsec");

            if (!string.IsNullOrEmpty(sleepDurationoInSecEnv))
            {
                sleepDurationoInSec = int.Parse(sleepDurationoInSecEnv);
            }

            await SendInfsoftTrackingAssetTelemetry(ioTHubModuleClient, cts.Token, apiKey, locationId, sleepDurationoInSec);
        }

        private static async Task SendInfsoftTrackingAssetTelemetry(ModuleClient ioTHubModuleClient, CancellationToken token, Guid apiKey, int locationId, int sleepDurationoInSec)
        {
            Tracking trackingAsset = new Tracking(api);

            while (!token.IsCancellationRequested)
            {                
                IEnumerable<AssetResult> assets = (IEnumerable<AssetResult>)trackingAsset.GetAsset(apiKey, locationId);

                foreach (var asset in assets)
                {
                    string assetJson = JsonSerializer.Serialize(asset);

                    var assetMessage = new Message(Encoding.UTF8.GetBytes(assetJson));
                    assetMessage.ContentType = "application/json";
                    assetMessage.ContentEncoding = "UTF-8";
                    assetMessage.Properties.Add("AssetUidId", asset.AssetUid.ToString());
                    assetMessage.Properties.Add("AssetName", asset.AssetName);

                    try
                    {
                        await ioTHubModuleClient.SendEventAsync("assetoutput", assetMessage);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error during message sending to Edge Hub: " + e.Message);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(10).Milliseconds, token);
            }
        }

        /// <summary>
        /// Callback for whenever the connection status changes
        /// Mostly we just log the new status and the reason. 
        /// But for some disconnects we need to handle them here differently for our module to recover
        /// </summary>
        /// <param name="status"></param>
        /// <param name="reason"></param>
        private static void ConnectionStatusHandler(ConnectionStatus status, ConnectionStatusChangeReason reason)
        {
            Console.WriteLine($"Module connection changed. New status={status.ToString()} Reason={reason.ToString()}");

            // Sometimes the connection can not be recovered if it is in either of those states.
            // To solve this, we exit the module. The Edge Agent will then restart it (retrying with backoff)
            if (reason == ConnectionStatusChangeReason.Retry_Expired || reason == ConnectionStatusChangeReason.Client_Close)
            {
                Console.WriteLine($"Connection can not be re-established. Exiting module");
                cts?.Cancel();
            }
        }

        /// <summary>
        /// This method is called whenever the module is sent a message from the EdgeHub. 
        /// It just pipe the messages without any change.
        /// It prints all the incoming messages.
        /// </summary>
        static async Task<MessageResponse> PipeMessage(Message message, object userContext)
        {
            int counterValue = Interlocked.Increment(ref counter);

            var moduleClient = userContext as ModuleClient;
            if (moduleClient == null)
            {
                throw new InvalidOperationException("UserContext doesn't contain " + "expected values");
            }

            byte[] messageBytes = message.GetBytes();
            string messageString = Encoding.UTF8.GetString(messageBytes);
            Console.WriteLine($"Received message: {counterValue}, Body: [{messageString}]");

            if (!string.IsNullOrEmpty(messageString))
            {
                using (var pipeMessage = new Message(messageBytes))
                {
                    foreach (var prop in message.Properties)
                    {
                        pipeMessage.Properties.Add(prop.Key, prop.Value);
                    }
                    await moduleClient.SendEventAsync("output1", pipeMessage);

                    Console.WriteLine("Received message sent");
                }
            }
            return MessageResponse.Completed;
        }
    }
}
