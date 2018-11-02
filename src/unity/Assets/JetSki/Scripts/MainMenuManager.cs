using System.Collections;
using System.Collections.Generic;
using System.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JetSki
{
    public class MainMenuManager : MonoBehaviour
    {
        private GameObject serverIpField;
        private GameObject startGameButton;
        private GameManager gameManager;

        void Start ()
        {
            gameManager = GameManager.instance;
            startGameButton = GameObject.Find("StartGameButton");
            serverIpField = GameObject.Find("ServerIpField");
            startGameButton.GetComponent<Button>().onClick.AddListener(StartGame);
        }
	
        void Update ()
        {
            if (!Globals.inMainMenu)
            {
                Globals.doingSetup = true; //no longer in main menu, now time to load arena
                Debug.Log("Connecting to " + Globals.ipConnect);
                gameManager.serverIp = IPAddress.Parse(Globals.ipConnect);
                gameManager.connection = new UdpConnectedClient(ip: gameManager.serverIp);
                gameManager.clientList.Add( //SERVER
                    new GameManager.Client {
                        ipEndPoint = new IPEndPoint(gameManager.serverIp, Globals.port)
                    }
                );
                /*gameManager.clientList.Add( //BALL
                    new GameManager.Client {
                        id = 9001,
                        name = "Ball"
                    }
                );*/
                //AddClient(new IPEndPoint(serverIp, Globals.port));
                
                /*arenaImage = GameObject.Find("ArenaImage");
                arenaText = GameObject.Find("ArenaName").GetComponent<Text>();
                arenaText.text = "Arena: " + Globals.arena;
                arenaImage.SetActive(true);
                
                Invoke("HideArenaImage", gameStartDelay);*/

                //clientList.Clear();
                
                gameManager.StartGame();
            }
        }

        void StartGame ()
        {
            var ip = serverIpField.GetComponent<InputField>().text;
            if (!ip.Equals(null))
            {
                Globals.ipConnect = ip;
                Globals.inMainMenu = false;
            }
        }
    }
}