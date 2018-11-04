using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using UnityEngine;

namespace JetSki
{
    public static class Globals
    {
        public const int port = 56789;
        public const int barrelCount = 6;
        public static string ipConnect = "127.0.0.1";
        public static string arena; //Current arena name
        public static bool inMainMenu; //we even in the game yet?
        public static bool doingSetup; //placing players down?
        public static bool gameOn; //game happening?
        public static uint myId;
        public static string myName;
    }
}
