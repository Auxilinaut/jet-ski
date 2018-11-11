using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using UnityEngine;
using System.Net;
using System.Net.Sockets;

namespace JetSki
{
  public class UdpConnectedClient
  {
    #region Data
    /// <summary>
    /// For Clients, the connection to the server.
    /// For Servers, the connection to a client.
    /// </summary>
    readonly UdpClient connection;
    #endregion

    #region Init
    public UdpConnectedClient(IPAddress ip = null)
    {
      if(!GameManager.instance.isClient)
      {
        connection = new UdpClient(Globals.port);
        connection.BeginReceive(OnServerReceive, null);
      }
      else
      {
        connection = new UdpClient(); // Auto-bind port
        connection.BeginReceive(OnClientReceive, null);
      }
      
    }

    public void Close()
    {
      connection.Close();
    }
    #endregion

    #region API
    void OnClientReceive(IAsyncResult ar)
    {
      try
      {
        IPEndPoint ipEndpoint = null;
        byte[] data = connection.EndReceive(ar, ref ipEndpoint);
                
        ClientManager.HandleData(data, ipEndpoint);
      }
      catch(SocketException e)
      {
        // This happens when a client disconnects, as we fail to send to that port.
      }
      connection.BeginReceive(OnClientReceive, null);
    }

    void OnServerReceive(IAsyncResult ar)
    {
      try
      {
        IPEndPoint ipEndpoint = null;
        byte[] data = connection.EndReceive(ar, ref ipEndpoint);
                
        ServerManager.HandleData(data, ipEndpoint);
      }
      catch(SocketException e)
      {
        // This happens when a client disconnects, as we fail to send to that port.
      }
      connection.BeginReceive(OnServerReceive, null);
    }

    internal void Send(byte[] data, IPEndPoint ipEndpoint)
    {
      connection.Send(data, data.Length, ipEndpoint);
    }
    #endregion
  }
}
