using System.Collections;
using System.Collections.Generic;
using System.Net;
using UnityEngine;
using UnityEngine.UI;                   //Allows us to use UI.

namespace JetSki
{
    public class MainMenuManager : MonoBehaviour
    {
        private GameManager gameManager;

        void Start ()
        {
            gameManager = GameManager.instance;
        }
	
        void Update ()
        {
            if (!Globals.inMainMenu)
            {
                Globals.doingSetup = true; //no longer in main menu, now time to load arena

                gameManager.serverIp = IPAddress.Parse(Globals.ipConnect);
                gameManager.connection = new UdpConnectedClient(ip: gameManager.serverIp);
                gameManager.clientList.Add(new IPEndPoint(gameManager.serverIp, Globals.port));
                //AddClient(new IPEndPoint(serverIp, Globals.port));
                
                /*arenaImage = GameObject.Find("ArenaImage");
                arenaText = GameObject.Find("ArenaName").GetComponent<Text>();
                arenaText.text = "Arena: " + Globals.arena;
                arenaImage.SetActive(true);
                
                Invoke("HideArenaImage", gameStartDelay);*/

                //clientList.Clear();
                
                gameManager.GetComponent<ClientManager>().enabled = true;
            }
        }
    }
}