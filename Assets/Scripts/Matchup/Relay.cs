﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MasterServer;
using UnityEngine;

// A single instance, as part of the startup scene, which is used to communicate
// with the connected server or clients.
public class Relay : MonoBehaviour
{
    // Global, ewww, but probably the only one we'll need in the end.
    public static Relay Instance { get; private set; }
    public Server BaseServer;

    public int CurrentVersionID = 0;

    public GameObject MainCamera;

    public bool DevelopmentMode = false;
	public bool AutoHost = false;

    private Server _CurrentServer;

    private bool TryingToConnect = false;

    // Some more un-lovely hacks
    private GUIStyle BoxSpacer;

    private float TimeUntilRefresh = 0f;
    private float TimeBetweenRefreshes = 15f;

    public const int CharacterSpawnGroupID = 1;

    public readonly string[] ListedMaps = new[]
    {
        "pi_mar",
        "pi_set",
        "pi_ven",
        "pi_rah"
    };

    public OptionsMenu OptionsMenu { get; private set; }
    public bool ShowOptions { get; set; }

    public AnimationCurve MouseSensitivityCurve;

    public int PublicizedVersionID
    {
        get { return DevelopmentMode ? -1 : CurrentVersionID; }
    }

    public static float DesiredTimeBetweenNetworkSends
    {
        // Cheat a little and give some headroom. Unity is sometimes a frame or two late.
        get { return 1f / 58f; }
    }
    public static float SpecifiedTimeBetweenNetworkSends
    {
        get { return 1f / Network.sendRate; }
    }

    public Server CurrentServer
    {
        get
        {
            return _CurrentServer;
        }
        set
        {
            if (_CurrentServer != null)
            {
                _CurrentServer.OnReceiveServerMessage -= ReceiveServerMessage;
            }
            _CurrentServer = value;
            if (_CurrentServer != null)
            {
                _CurrentServer.OnReceiveServerMessage += ReceiveServerMessage;
            }
            else
            {
                TimeUntilRefresh = 1f;
                // Refresh if we go back to the title screen
            }
            TryingToConnect = false;
        }
    }

    public GUISkin BaseSkin;
    public MessageLog MessageLog { get; private set; }

    private ExternalServerList ExternalServerList;

    [Serializable]
    public enum RunMode
    {
        Client, Server
    }

    public const int Port = 31415;
    public string ConnectingServerHostname = "127.0.0.1";

    public Color GoodConnectionColor;
    public Color BadConnectionColor;

    public void Awake()
    {
        DontDestroyOnLoad(this);
        Instance = this;
        MessageLog = new MessageLog();
        MessageLog.Skin = BaseSkin;

        Network.natFacilitatorIP = "107.170.78.82";

        ExternalServerList = new ExternalServerList();
        ExternalServerList.OnMasterServerListChanged += ReceiveMasterListChanged;
        ExternalServerList.OnMasterServerListFetchError += ReceiveMasterListFetchError;

        BoxSpacer = new GUIStyle(BaseSkin.box) {fixedWidth = 1};

        OptionsMenu = new OptionsMenu(BaseSkin);

        OptionsMenu.OnOptionsMenuWantsClosed += () =>
        { ShowOptions = false; };
        OptionsMenu.OnOptionsMenuWantsQuitGame += Application.Quit;
        OptionsMenu.OnOptionsMenuWantsGoToTitle += Network.Disconnect;

        // We want 60fps send rate, but Unity seems retarded and won't actually
		// send at the rate you give it. So we'll just specify it higher and
		// hope to meet the minimum of 60 ticks per second.
        Network.sendRate = 80;
    }

    private string GetRandomMapName()
    {
        List<string> maps = ListedMaps.ToList();
        while (maps.Count > 0)
        {
            int idx = UnityEngine.Random.Range(0, maps.Count);
            string mapName = maps[idx];
            if (Application.CanStreamedLevelBeLoaded(mapName))
                return mapName;
            maps.RemoveAt(idx);
        }
        // Bummer :(
        return "";
    }

    public void Start()
    {
        Application.LoadLevel(GetRandomMapName());

        //ExternalServerList.Refresh();
    }

    public void Connect(RunMode mode)
    {
        switch (mode)
        {
            case RunMode.Client:
                TryingToConnect = true;
                Network.Connect(ConnectingServerHostname, Port);
                MessageLog.AddMessage("Connecting to " + ConnectingServerHostname + ":" + Port);
                break;
            case RunMode.Server:
                TryingToConnect = true;
                Network.InitializeServer(32, Port, true); // true = use nat facilitator
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void ConnectToExternalListedServer(ServerInfoRaw serverInfo)
    {
        TryingToConnect = true;
        Network.Connect(serverInfo.ip);
        MessageLog.AddMessage("Connecting to " + serverInfo.ip);
    }
    private void ConnectToRandomServer()
    {
        ServerInfoRaw serverInfo;
        if (ExternalServerList.TryGetRandomServer(out serverInfo))
        {
            ConnectToExternalListedServer(serverInfo);
        }
    }

    public void OnServerInitialized()
    {
        TryingToConnect = false;
        MessageLog.AddMessage("Started server on port " + Port);
        var server = (Server)Network.Instantiate(BaseServer, Vector3.zero, Quaternion.identity, 0 );
        server.NetworkGUID = Network.player.guid;
        MessageLog.AddMessage("Server GUID: " + server.NetworkGUID);
        // Old method, would still be useful if we ever have multiple servers per Unity process (wha?)
        //CurrentServer = (Server)Network.Instantiate(BaseServer, Vector3.zero, Quaternion.identity, 0 );
        //CurrentServer.Relay = this;
    }

    public void OnFailedToConnect(NetworkConnectionError error)
    {
        MessageLog.AddMessage("Failed to connect: " + error);
        TryingToConnect = false;
    }

    public void OnDisconnectedFromServer(NetworkDisconnection error)
    {
        MessageLog.AddMessage("Disconnected from server: " + error);
        if (CurrentServer != null)
            Destroy(CurrentServer.gameObject);
        TryingToConnect = false;
    }

    public void Update()
    {
        MessageLog.Update();
        if (CurrentServer != null)
        {
            if (Input.GetKeyDown("f8"))
            {
                Network.Disconnect();
            }
        }

        // Hide/show options
        if (Input.GetKeyDown(KeyCode.Escape) && !MessageLog.HasInputOpen)
            ShowOptions = !ShowOptions;

        if (CurrentServer == null)
        {
	        if (AutoHost)
	        {
				if (RespawnZone.HasRespawnPoints)
					Connect(RunMode.Server);
	        }
	        else
	        {

		        TimeUntilRefresh -= Time.deltaTime;
		        if (TimeUntilRefresh <= 0f)
		        {
			        TimeUntilRefresh += TimeBetweenRefreshes;
			        ExternalServerList.Refresh();
		        }

		        OptionsMenu.ShouldDisplaySpectateButton = false;
	        }
        }

        var sb = new StringBuilder();
        sb.AppendLine(PlayerScript.UnsafeAllEnabledPlayerScripts.Count + " PlayerScripts");
        sb.Append(PlayerPresence.UnsafeAllPlayerPresences.Count + " PlayerPresences");
        ScreenSpaceDebug.AddLineOnce(sb.ToString());

        for (int i = 0; i < PlayerScript.UnsafeAllEnabledPlayerScripts.Count; i++)
        {
            var character = PlayerScript.UnsafeAllEnabledPlayerScripts[i];
            var presenceName = character.Possessor == null ? "null" : character.Possessor.Name;
            ScreenSpaceDebug.AddLineOnce("Character: " + character.name + " possessed by " + presenceName);
        }
        for (int i = 0; i < PlayerPresence.UnsafeAllPlayerPresences.Count; i++)
        {
            var presence = PlayerPresence.UnsafeAllPlayerPresences[i];
            var characterName = presence.Possession == null ? "null" : presence.Possession.name;
            ScreenSpaceDebug.AddLineOnce("Presence: " + presence.Name + " possessing " + characterName);
        }

        OptionsMenu.Update();
        Screen.showCursor = !Screen.lockCursor;
    }

    private bool ExternalServerListAvailable
    {
        get
        {
            if (ExternalServerList == null) return false;
            if (ExternalServerList.MasterListRaw == null) return false;
            return true;
        }
    }
    private bool DoAnyServersExist
    {
        get
        {
            if (ExternalServerList == null) return false;
            if (ExternalServerList.MasterListRaw == null) return false;
            return ExternalServerList.MasterListRaw.servers.Length > 0;
        }
    }

    private int ServerListEntries
    {
        get
        {
            if (DoAnyServersExist)
                return ExternalServerList.MasterListRaw.servers.Length;
            else
                return 1;
        }
    }

    private int ServerListHeight
    {
        get
        {
            return ServerListEntries * 36;
        }
    }

    public void OnGUI()
    {
        MessageLog.OnGUI();

        // Display name setter and other stuff when not connected
        if (CurrentServer == null)
        {
            GUI.skin = BaseSkin;
            // TODO less magical layout numerology
            GUILayout.Window(Definitions.LoginWindowID, new Rect( ( Screen.width / 2 ) - 155, Screen.height - 110, 77, 35), DrawLoginWindow, string.Empty);
    	    GUILayout.Window(Definitions.ServerListWindowID, new Rect( ( Screen.width / 2 ) - 155, Screen.height - ServerListHeight - 110, 312, ServerListHeight), DrawServerList, string.Empty);
        }

        if (ShowOptions)
        {
            Screen.lockCursor = false;
        }
        OptionsMenu.DrawGUI();
    }

    private void DrawLoginWindow(int id)
    {
		GUILayout.BeginHorizontal();
        {
            string currentUserName = PlayerPrefs.GetString("username", "Anonymous");
            string newStartingUserName = RemoveSpecialCharacters(GUILayout.TextField(currentUserName));
            if (newStartingUserName != currentUserName)
            {
                PlayerPrefs.SetString("username", newStartingUserName);
                PlayerPrefs.Save();
            }
            GUI.enabled = !TryingToConnect;
            // TODO shouldn't be allocating here, that's dumb, store it
			GUILayout.Box( "", BoxSpacer );
            if(GUILayout.Button("HOST"))
            {
                GlobalSoundsScript.PlayButtonPress();
                Connect(RunMode.Server);
            }
			GUILayout.Box( "", BoxSpacer );
            if (DevelopmentMode)
            {
                if(GUILayout.Button("LOCAL"))
                {
                    GlobalSoundsScript.PlayButtonPress();
                    Connect(RunMode.Client);
                }
            }
            else
            {
                if(GUILayout.Button("RANDOM"))
                {
                    GlobalSoundsScript.PlayButtonPress();
                    ConnectToRandomServer();
                }
            }

			GUILayout.Box( "", BoxSpacer );
            if(GUILayout.Button("REFRESH"))
            {
                GlobalSoundsScript.PlayButtonPress();
                ExternalServerList.Refresh();
            }

            GUI.enabled = true;
        }
        GUILayout.EndHorizontal();
    }

    private void DrawServerList(int id)
    {
        // TODO this should be in a scrollable view, because it will obviously run offscreen if there are too many
        if (DoAnyServersExist)
        {
            GUIStyle rowStyle = new GUIStyle( BaseSkin.box ) { fixedWidth = 312 - 65 };
            StringBuilder sb = new StringBuilder();
            foreach (var serverInfo in ExternalServerList.MasterListRaw.servers)
            {
                sb.Append(serverInfo.name);
                sb.Append(", ");
                sb.Append(serverInfo.players);
                sb.Append(" players on ");
                sb.Append(serverInfo.map);

                if( serverInfo.VersionMismatch )
                    sb.Append( " |Game Using Incompatible Version|" );

                GUILayout.BeginHorizontal();
                //rowStyle.normal.textColor = PlayerRegistry.For(log.Player).Color;
                GUILayout.Box(sb.ToString(), rowStyle);
    			GUILayout.Box( "", new GUIStyle( BaseSkin.box ) { fixedWidth = 1 } );
                GUI.enabled = !TryingToConnect && !serverInfo.VersionMismatch;

                if(GUILayout.Button("JOIN"))
                {
                    GlobalSoundsScript.PlayButtonPress();
                    ConnectToExternalListedServer(serverInfo);
                }
                GUILayout.EndHorizontal();

                // clear
                sb.Length = 0;
            }
        }
        else
        {
            GUIStyle rowStyle = new GUIStyle( BaseSkin.box ) { fixedWidth = 312 };
            GUILayout.BeginHorizontal();
            //rowStyle.normal.textColor = PlayerRegistry.For(log.Player).Color;
            string message;
            if (ExternalServerListAvailable)
            {
                message = "No servers";
            }
            else
            {
                message = "Getting server list...";
            }
            GUILayout.Box(message, rowStyle);
            GUILayout.EndHorizontal();
        }
    }

    public bool IsConnected { get { return Network.peerType != NetworkPeerType.Disconnected; } }

    private void ReceiveServerMessage(string text)
    {
        MessageLog.AddMessage(text);
    }

    // Really? Nothing like this exists? hmm
    // Also, can still be 'sploited by unicode shenanigans
    private static string RemoveSpecialCharacters(string str) 
    {
       var sb = new StringBuilder();
       foreach (char c in str)
          if (c != '\n' && c != '\r' && sb.Length < 24)
             sb.Append(c);
       return sb.ToString();
    }

    public void OnDestroy()
    {
        ExternalServerList.OnMasterServerListChanged -= ReceiveMasterListChanged;
        ExternalServerList.OnMasterServerListFetchError -= ReceiveMasterListFetchError;
        ExternalServerList.Dispose();
    }

    private void ReceiveMasterListChanged()
    {
        // do something useful?
    }

    private void ReceiveMasterListFetchError(string message)
    {
        MessageLog.AddMessage("Failed to get server list: " + message);
    }
}