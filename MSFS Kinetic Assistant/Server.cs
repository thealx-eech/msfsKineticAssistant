/*
 Location Bridge

 http://eartoearoak.com/software/location-bridge

 Copyright 2014-2015 Al Brown

 A simple server that provides NMEA sentences over TCP from the
 Windows location sensors.


 This program is free software: you can redistribute it and/or modify
 it under the terms of the GNU General Public License as published by
 the Free Software Foundation, or (at your option)
 any later version.

 This program is distributed in the hope that it will be useful,
 but WITHOUT ANY WARRANTY; without even the implied warranty of
 MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 GNU General Public License for more details.

 You should have received a copy of the GNU General Public License
 along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Timers;
using MSFS_Kinetic_Assistant.Properties;

namespace MSFS_Kinetic_Assistant
{

    // Based on http://msdn.microsoft.com/en-us/library/fx6588te.aspx

    class Client
    {
        public Socket socket { get; set; }

        public Client(Socket socket)
        {
            this.socket = socket;
        }
    }

    class Server
    {
        public const int MAX_CLIENTS = 10;

        private static byte[] warnMaxClients = Encoding.ASCII.GetBytes(
            Resources.TooManyConnections);

        private object lockGps;
        private CallbackUI callback;

        private volatile bool cancel = false;
        private Object lockClients = new Object();
        private List<Client> clients = new List<Client>();
        private IPAddress ipAddress;

        public static ManualResetEvent signal = new ManualResetEvent(false);

        public Server(object gpsLock, CallbackUI callback, string ip)
        {
            ipAddress = IPAddress.Parse(ip);
            lockGps = gpsLock;
            this.callback = callback;
            UpdateCount();

            System.Timers.Timer timerAlive = new System.Timers.Timer();
            timerAlive.Elapsed += new ElapsedEventHandler(OnTimerAlive);
            timerAlive.Interval = 1000;
            timerAlive.Enabled = true;
        }

        public void Start()
        {
            IPEndPoint localEndPoint;
            /*if (Properties.Settings.Default.LocalOnly)
                localEndPoint = new IPEndPoint(IPAddress.Loopback, 10110);
            else*/
                localEndPoint = new IPEndPoint(ipAddress, 10110);

            var socketServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            callback(new ServerStatus(Resources.Waiting));

            try
            {
                socketServer.Bind(localEndPoint);
                socketServer.Listen(100);

                while (!cancel)
                {
                    signal.Reset();
                    socketServer.BeginAccept(new AsyncCallback(ClientCallback),
                        socketServer);
                    signal.WaitOne();
                }
            }
            catch (SocketException e)
            {
                Console.Write("EXCEPTION: Start error " + e.Message);
                callback(new ServerStatus(ServerStatusCode.ERROR,
                    String.Format(Resources.Error0, e.SocketErrorCode.ToString())));
                if (socketServer.IsBound)
                    socketServer.Shutdown(SocketShutdown.Both);
            }
            finally
            {
                socketServer.Close();
                foreach (var client in clients.ToArray())
                    ClientRemove(client);
            }
        }

        private static string GetHostName(string address)
        {
            /*string host = address.Split(':')[0];
            string hostName = "";
            try
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(host);

                hostName = hostEntry.HostName;
            }
            catch (SocketException ex)
            {
                Console.Write("EXCEPTION: Can't get host name " + ex.Message);
                hostName = host;
            }

            return "'" + hostName + "'";*/

            try
            {
                IPHostEntry entry = Dns.GetHostEntry(address);
                if (entry != null)
                {
                    return entry.HostName;
                }
            }
            catch (SocketException ex)
            {
                Console.Write("EXCEPTION: Can't get host name " + ex.Message);
            }

            return "";
        }


        private void ClientAdd(Client client)
        {
            lock (lockClients)
                clients.Add(client);
        }

        private void ClientRemove(Client client)
        {
            lock (lockClients)
                clients.Remove(client);
            var addr = GetHostName(client.socket.RemoteEndPoint.ToString());
            client.socket.Shutdown(SocketShutdown.Both);
            client.socket.Close();
            callback(new ServerStatus(String.Format(Resources._0Disconnected, addr)));
            UpdateCount();
        }

        private void ClientCallback(IAsyncResult ar)
        {
            signal.Set();

            try
            {
                var socketServer = (Socket)ar.AsyncState;
                var socketClient = socketServer.EndAccept(ar);
                var addr = GetHostName(socketClient.RemoteEndPoint.ToString());

                if (clients.Count < MAX_CLIENTS)
                {
                    var client = new Client(socketClient);
                    ClientAdd(client);
                    callback(new ServerStatus(
                        String.Format(Resources._0Connected, addr)));
                    UpdateCount();
                }
                else
                {
                    socketClient.Send(warnMaxClients);
                    socketClient.Shutdown(SocketShutdown.Both);
                    socketClient.Close();
                    callback(new ServerStatus(
                        String.Format(Resources._0RefusedTooManyConnections, addr)));
                }
            }
            catch (Exception ex) {
                Console.Write("EXCEPTION: Can't process callback " + ex.Message);
            }
        }

        private void Send(Client client, String data)
        {
            var byteData = Encoding.ASCII.GetBytes(data);
            try
            {
                client.socket.BeginSend(byteData, 0, byteData.Length, 0,
                                        new AsyncCallback(SendCallback), client);
            }
            catch (SocketException ex)
            {
                Console.Write("EXCEPTION: Send error " + ex.Message);
                ClientRemove(client);
            }
        }

        private void SendCallback(IAsyncResult ar)
        {
            var client = (Client)ar.AsyncState;
            try
            {
                client.socket.EndSend(ar);
            }
            catch (SocketException ex)
            {
                Console.Write("EXCEPTION: SendCallback error " + ex.Message);
                ClientRemove(client);
            }
        }

        private bool IsConnected(Socket socket)
        {
            try
            {
                return !(socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch (SocketException ex) {
                Console.Write("EXCEPTION: IsConnected error " + ex.Message);
                return false;
            }
        }

        private void OnTimerAlive(object source, ElapsedEventArgs e)
        {
            List<Client> disconnected = new List<Client>();

            lock (lockClients)
            {
                foreach (Client client in clients)
                    if (!IsConnected(client.socket))
                        disconnected.Add(client);
                foreach (Client client in disconnected)
                    ClientRemove(client);
            }
        }

        private static string NmeaCoord(double coord, bool isLat)
        {
            string direct;

            if (isLat)
            {
                if (coord < 0)
                    direct = "S";
                else
                    direct = "N";
            }
            else
            {
                if (coord < 0)
                    direct = "W";
                else
                    direct = "E";
            }

            coord = Math.Abs(coord);

            int deg = (int)Math.Floor(coord);
            double min = (coord % 1) * 60;
            var nmea = deg.ToString() + min.ToString("00.0000000", CultureInfo.InvariantCulture) + "," + direct;
            return nmea;
        }

        private static string NmeaChecksum(string sentence)
        {
            var checksum = 0;

            for (var i = 0; i < sentence.Length; i++)
                checksum ^= sentence[i];

            return "*" + checksum.ToString("X2");
        }

        private static string NmeaFormat(Location location)
        {
            var lat = NmeaCoord(location.lat, true);
            var lon = NmeaCoord(location.lon, false);
            var alt = location.alt.ToString("F1");
            var speed = (location.speed * 1.9).ToString("F3") ;
            var time = location.time.ToString("HHmmss.ff");
            var date = location.time.ToString("ddmmyy");
            var ha = location.ha.ToString("0.0");

            var gga = String.Format("GPGGA,{0},{1},{2},1,12,1,{3},M,,,,", time, lat, lon, alt);
            var gll = String.Format("GPGLL,{0},{1},{2},A", lat, lon, time);
            var rmc = String.Format("GPRMC,{0},A,{1},{2},{3},{4},{5},0.0,E,A", time, lat, lon, speed, ha, date);
            var gsa = "GPGSA,A,3,04,05,06,09,12,20,22,24,25,26,28,30,1,1,1,";
            var vgt = String.Format("GPVTG,{0},T,{0},M,0,N,0,K", ha);

            gga = "$" + gga + NmeaChecksum(gga) + "\r\n";
            gll = "$" + gll + NmeaChecksum(gll) + "\r\n";
            rmc = "$" + rmc + NmeaChecksum(rmc) + "\r\n";
            gsa = "$" + gsa + NmeaChecksum(gsa) + "\r\n";
            vgt = "$" + vgt + NmeaChecksum(vgt) + "\r\n";

            string responce = gga + gll + rmc + gsa + vgt;

            return responce;

            /*
+$GPGGA,101338.00,4630.9471,N,00719.523,E,1,12,1,3623.8,M,,,,*26
+$GPGLL,4630.9471,N,00719.523,E,101338.00,A*3D
+$GPRMC,101338.00,A,4630.9471,N,00719.523,E,0,33.83,250221,0.91,E*4A
$GPGSV,3,1,12,04,26,291,92,05,32,062,86,06,05,029,33,09,47,095,98*78
$GPGSV,3,2,12,12,60,224,88,20,21,240,85,22,28,311,93,24,81,037,77*78
$GPGSV,3,3,12,25,160,124,85,26,321,240,90,28,90,200,90,30,181,137,87*65
+$GPGSA,A,3,04,05,06,09,12,20,22,24,25,26,28,30,1,1,1,*02
$GPVTG,33.84,T,32.93,M,0,N,0,K*49
            */
        }

        private void UpdateCount()
        {
            callback(new ServerStatus(ServerStatusCode.CONN,
                clients.Count));
        }

        public void GpsUpdate(Location location)
        {
            lock (lockGps)
                location = new Location(location);

            lock (lockClients)
                foreach (var client in clients.ToArray())
                    Send(client, NmeaFormat(location));
        }

        public void Stop()
        {
            cancel = true;
            signal.Set();
        }
    }

    enum ServerStatusCode { OK, ERROR, CONN };

    class ServerStatus
    {
        public ServerStatusCode status { get; set; }
        public string message { get; set; }
        public int value { get; set; }

        public ServerStatus(string message)
        {
            this.status = ServerStatusCode.OK;
            this.message = message;
            this.value = 0;
        }

        public ServerStatus(ServerStatusCode status, string message)
        {
            this.status = status;
            this.message = message;
            this.value = 0;
        }

        public ServerStatus(ServerStatusCode status, int value)
        {
            this.status = status;
            this.message = null;
            this.value = value;
        }

    }

    class Location
    {
        public double lat { get; set; }
        public double lon { get; set; }
        public double alt { get; set; }
        public double speed { get; set; }
        public double ha { get; set; }
        public double va { get; set; }
        public DateTimeOffset time { get; set; }

        public Location()
        {
        }

        public Location(Location original)
        {
            lat = original.lat;
            lon = original.lon;
            alt = original.alt;
            speed = original.speed;
            ha = original.ha;
            va = original.va;
            time = original.time;
        }
    }

    delegate void CallbackUI(ServerStatus status);
}
