
using System.Collections;
using System.Collections.Generic;       //Allows us to use Lists. 
using UnityEngine;
using UnityEngine.UI;                   //Allows us to use UI.
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
        //public Text ipServer;
        public Button startGameButton;
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
        public List<IPEndPoint> clientList = new List<IPEndPoint>();

        public static byte[] theData;

        UdpConnectedClient connection;
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

            mainMenuManager = GetComponent<MainMenuManager>();
            clientManager = GetComponent<ClientManager>();
            serverManager = GetComponent<ServerManager>();

            if (isClient)
            {
                this.ClientInitGame();
            }
            else
            {
                connection = new UdpConnectedClient();
                this.ServerInitGame(connection);
            }

        }
        
        void ClientInitGame()
        {
            //if (!SceneManager.GetActiveScene().name.Equals("mainmenu"))
            Globals.inMainMenu = true;
            SceneManager.LoadScene("mainmenu");

        }

        void ServerInitGame(UdpConnectedClient connection)
        {

        }
        
        //Hides black image used between arenas
        void HideArenaImage()
        {
            //Disable the arenaimage gameObject.
            arenaImage.SetActive(false);
        }
        
        void Update()
        {
            if (!Globals.inMainMenu)
            {
                Globals.doingSetup = true; //no longer in main menu, now time to load arena

                serverIp = IPAddress.Parse(Globals.ipConnect);
                connection = new UdpConnectedClient(ip: serverIp);
                instance.clientList.Add(new IPEndPoint(serverIp, Globals.port));
                //AddClient(new IPEndPoint(serverIp, Globals.port));
                
                arenaImage = GameObject.Find("ArenaImage");
                arenaText = GameObject.Find("ArenaName").GetComponent<Text>();
                arenaText.text = "Arena: " + Globals.arena;
                arenaImage.SetActive(true);
                
                Invoke("HideArenaImage", gameStartDelay);

                //clientList.Clear();
                
                clientManager.enabled = true;
            }
        }

        internal static void AddClient(IPEndPoint ipEndpoint)
        {
            if (instance.clientList.Contains(ipEndpoint) == false)
            { // If it's a new client, add to the client list
                print($"Connect to {ipEndpoint}");
                instance.clientList.Add(ipEndpoint);
            }
        }

        /// <summary>
        /// TODO: We need to add timestamps to timeout and remove clients from the list.
        /// </summary>
        internal static void RemoveClient(IPEndPoint ipEndpoint)
        {
            instance.clientList.Remove(ipEndpoint);
        }

        internal static void HandleData(byte[] data, IPEndPoint ipEndpoint)
        {
            if (instance.isClient) //Client Data Handler
            {
                
            }
            else //Server Data Handler
            {
                if (Globals.gameOn)
                {
                    GameOnMessage msg = GameOnMessage.Parser.ParseFrom(data);
                    Debug.Log("GameOnMessage: " + msg.ToString());
                    switch ((int)msg.GameOnCase)
                    {
                        case 1: //*****GET INPUT MESSAGE (UNIMPLEMENTED)*****
                            Debug.Log("ID: " + msg.ClientInputMsg.Id);
                        break;

                        case 2: //*****SCORE UDPATE (UNIMPLEMENTED)*****
                            Debug.Log("Somebody scored.");
                            Debug.Log("ID: " + msg.ScoreMsg.Id);
                            Debug.Log("Score: " + msg.ScoreMsg.Score);
                        break;

                        case 3: //*****STOP GAME (UNTESTED)*****
                            Globals.gameOn = false;
                        break;
                    }
                }
                else
                {
                    GameOffMessage msg = GameOffMessage.Parser.ParseFrom(data);
                    Debug.Log("GameOffMessage: " + msg.ToString());
                    switch ((int)msg.GameOffCase)
                    {
                        case 1: //*****JOIN SERVER (UNIMPLEMENTED)*****
                            Debug.Log("Joining server.");
                            Debug.Log("Team: " + msg.AcceptJoinMsg.Team);
                            Debug.Log("Position: " + msg.AcceptJoinMsg.Position);
                            Debug.Log("Rotation: " + msg.AcceptJoinMsg.Rotation);
                            instance.JoinServer(msg.AcceptJoinMsg.Team, msg.AcceptJoinMsg.Position, msg.AcceptJoinMsg.Rotation);
                        break;

                        case 2: //*****NEW OTHER PLAYERS (UNIMPLEMENTED)*****
                            Debug.Log("New player joined.");
                            Debug.Log("ID: " + msg.NewPlayerMsg.Id);
                            Debug.Log("Team: " + msg.NewPlayerMsg.Team);
                            Debug.Log("Position: " + msg.NewPlayerMsg.Position);
                            Debug.Log("Rotation: " + msg.NewPlayerMsg.Rotation);
                            instance.NewOtherPlayer(msg.NewPlayerMsg.Id, msg.NewPlayerMsg.Team, msg.NewPlayerMsg.Position, msg.NewPlayerMsg.Rotation);
                        break;

                        case 3: //*****START GAME (UNTESTED)*****
                            instance.gameOn = true;
                        break;
                    }
                }
            }
        }

        private void OnApplicationQuit()
        {
            connection.Close();
        }
    }
}