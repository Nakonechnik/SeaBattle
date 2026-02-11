using System;

namespace SeaBattle.Client
{
    public class NetworkClient
    {
        public bool IsConnected { get; private set; }

        public NetworkClient()
        {
            IsConnected = false;
        }

        public void Connect()
        {
            IsConnected = true;
        }

        public void Disconnect()
        {
            IsConnected = false;
        }
    }
}