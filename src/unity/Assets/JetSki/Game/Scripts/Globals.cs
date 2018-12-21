using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using UnityEngine;

namespace JetSki
{
    public static class Globals
    {
        public const int barrelCount = 6; //number of barrels
        public static string ipConnect = "127.0.0.1"; //server IP
        public const int port = 56789; //server port
        public static string arena; //Current arena name
        public static bool inReplay; //watching a replay
        public static bool inMainMenu; //not in game yet
        public static bool doingSetup; //placing players down
        public static bool gameOn; //game happening
        public static uint myId; //player ID
        public static string myName; //player name
    }
}
