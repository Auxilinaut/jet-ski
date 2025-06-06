
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Net;
using System.Net.Sockets;
using System.IO;
using Google.Protobuf;
using JetSkiProto;
using static JetSkiProto.InputMessage.Types;

namespace JetSki{

    public class GameManager : MonoBehaviour
    {
        public struct Client
        {
            public uint id;
            public IPEndPoint ipEndPoint;
            public string name;
            public uint team;
            public Vector3 position;
            public Quaternion rotation;
        }

        public float gameStartDelay = 2f;                      //Time to wait before starting arena, in seconds.
        public bool isClient;
        public static GameManager instance = null;              //Static instance of GameManager which allows it to be accessed by any other script.
        
        private Text arenaText;                                 //Text to display current arena number.
        private GameObject arenaImage;                          //Image to block out arena as arenas are being set up, background for arenaText.
        
        private MainMenuManager mainMenuManager;
        private ClientManager clientManager;
        private ServerManager serverManager;
        
        // networking 
        private string ipConnect = Globals.ipConnect;
        private List<int> _pingTime = new List<int>();
        public float latency = 0f;

        #region Data

        /// <summary>
        /// IP for clients to connect to. Null if you are the server.
        /// </summary>
        public IPAddress serverIp;

        /// <summary>
        /// For Clients, there is only one and it's the connection to the server.
        /// For Servers, there are many - one per connected client.
        /// </summary>
        public List<Client> clientList = new List<Client>();

        public UdpConnectedClient connection;
        #endregion

        void Awake()
        {
            Application.targetFrameRate = 60;

            //Check if instance already exists
            if (instance == null)
                
                //if not, set instance to this
                instance = this;
            
            //If instance already exists and it's not this:
            else if (instance != this)
                
                //Then destroy this. This enforces our singleton pattern, meaning there can only ever be one instance of a GameManager.
                Destroy(gameObject);    
            
            //Sets this to not be destroyed when reloading scene
            DontDestroyOnLoad(gameObject);

            mainMenuManager = gameObject.GetComponent<MainMenuManager>();
            clientManager = gameObject.GetComponent<ClientManager>();
            serverManager = gameObject.GetComponent<ServerManager>();
            
            if (isClient)
            {
                this.ClientInitGame();
            }
            else
            {
                connection = new UdpConnectedClient();
                this.ServerInitGame();
            }

        }
        
        void ClientInitGame()
        {
            mainMenuManager.enabled = true;
            Globals.inMainMenu = true;
            SceneManager.LoadScene("mainmenu");
        }

        void ServerInitGame()
        {
            //random map between 2 maps for now
            if (Random.Range(0,2) == 1)
                Globals.arena = "instance";
            else
                Globals.arena = "hydrobase";

            serverManager.enabled = true;
            SceneManager.LoadScene(Globals.arena);
        }

        public void StartGame()
        {
            mainMenuManager.enabled = false;
            clientManager.enabled = true;
        }

        private void OnApplicationQuit()
        {
            instance.connection.Close();
        }
    }
}