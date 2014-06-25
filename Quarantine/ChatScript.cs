using System;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

public class ChatScript : MonoBehaviour
{
    public readonly List<ChatMessage> ChatLog = new List<ChatMessage>();

    public GUISkin Skin;

    // TODO was unused. Intentional?
    //GUIStyle ChatStyle;
    //GUIStyle MyChatStyle;

	string lastMessage = "";
    public bool showChat;
    bool ignoreT;
    bool forceVisible;

    public static ChatScript Instance { get; private set; }

    public void Awake()
    {
        Instance = this;
    }

    public void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Start()
    {
        // TODO was unused. Intentional?
        //MyChatStyle = new GUIStyle(ChatStyle) { normal = { background = Skin.window.normal.background } };
        //ChatStyle = new GUIStyle { normal = { textColor = Color.white }, padding = { top = 9, left = 5, right = 10 }, fixedWidth = 209, fixedHeight = 32 };
    }

    public void OnServerInitialized()
    {
        CleanUp();
    }

    public void OnConnectedToServer()
    {
        CleanUp();
    }

    public void OnDisconnectedFromServer()
    {
        Screen.lockCursor = false;
    }

    void CleanUp()
    {
        ChatLog.Clear();
        showChat = false;
        lastMessage = string.Empty;
    }

    public void Update()
    {
        forceVisible = Input.GetKey(KeyCode.Tab) || RoundScript.Instance.RoundStopped;

        foreach (var log in ChatLog)
            log.Life += Time.deltaTime;
    }

    public void OnGUI()
	{
	    if (Network.peerType == NetworkPeerType.Disconnected || Network.peerType == NetworkPeerType.Connecting) return;

	    GUI.skin = Skin;

        var enteredChat = !showChat && (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.T);
        if (enteredChat)
        {
            //Debug.Log("Should enter chat");
            showChat = true;
        }

	    var height = 36 + ChatLog.Count(x => !x.Hidden || forceVisible) * 36;
	    GUILayout.Window(1, new Rect(35, Screen.height - height, 247, height), Chat, string.Empty);

        if (enteredChat)
        {
            GUI.FocusWindow(1);
            GUI.FocusControl("ChatInput");
            ignoreT = Event.current.keyCode == KeyCode.T;
        }
	}

    void Chat(int windowId)
    {
        if( !PlayerRegistry.Propagated )
            return ;

        try
        {
            foreach (var log in ChatLog)
            {
                if (!PlayerRegistry.Has(log.Player))
                    continue;

                if (log.Life > 15) 
                    log.Hidden = true;

                if (log.Hidden && !forceVisible)
                    continue;

                GUIStyle rowStyle = new GUIStyle( Skin.box ) { fixedWidth = 200 };
                //if (log.Player == Network.player && !log.IsSystem) rowStyle = MyChatStyle;

                GUILayout.BeginHorizontal();
				
                if( !log.IsSourceless )
                    rowStyle.normal.textColor = PlayerRegistry.For(log.Player).Color;
				
				String message = ( log.IsSourceless ? "" : ( PlayerRegistry.For( log.Player ).Username.ToUpper() + ": " ) ) + log.Message;
				
                /*if( log.IsSystem )
                {
                    if( !log.IsSourceless )
                    {
                        var playerName = PlayerRegistry.For(log.Player).Username.ToUpper();
                        //rowStyle.fixedWidth = rowStyle.CalcSize(new GUIContent(playerName)).x;
                        GUILayout.Box( playerName, rowStyle );
                    }
                    else
                    {
                        rowStyle.fixedWidth = rowStyle.CalcSize(new GUIContent(" ")).x;
                        rowStyle.padding.left = 0;
                        GUILayout.Label(" ", rowStyle);
                    }
					
                    GUILayout.Box( " " + log.Message, rowStyle );
                }
                else
                {
                    GUILayout.Box( PlayerRegistry.For(log.Player).Username.ToUpper() + ":", rowStyle );
                    GUILayout.Box( log.Message, rowStyle, GUILayout.MaxWidth(225)); 
                }*/
				GUILayout.Box( message );
                
                GUILayout.EndHorizontal();
            }
			
            GUILayout.BeginHorizontal();

            if (showChat)
            {
                GUI.SetNextControlName("ChatInput");
				
				GUIStyle sty = new GUIStyle( Skin.textField ) { fixedWidth = 180 };
                lastMessage = GUILayout.TextField(lastMessage, sty);

                if (ignoreT)
                {
                    if (lastMessage.ToLower() == "t")
                    {
                        lastMessage = string.Empty;
                        ignoreT = false;
                    }
                }

                if (Event.current.keyCode == KeyCode.Return && (Event.current.type == EventType.KeyDown || Event.current.type == EventType.Layout))
                {
                    lastMessage = lastMessage.Trim();
                    if (lastMessage.StartsWith("/"))
                    {
                        var messageParts = lastMessage.Split(' ');

                        // console commands
                        switch (messageParts[0])
                        {
                            case "/leave":
                                TaskManager.Instance.WaitFor(0.1f).Then(() =>
                                {
                                    GlobalSoundsScript.PlayButtonPress();
                                    Network.Disconnect();
                                    ServerScript.Spectating = false;
                                });
                                break;

                            case "/quit":
                                Application.Quit();
                                break;

                            case "/map":
                                if (!Network.isServer)
                                    LogChat(Network.player, "Map change is only allowed on server.", true, true);
                                else if (messageParts.Length != 2)
                                    LogChat(Network.player, "Invalid arguments, expected : /map map_name", true,
                                            true);
                                else if (Application.loadedLevelName == messageParts[1])
                                    LogChat(Network.player, "You're already in " + messageParts[1] + ", dummy.",
                                            true, true);
                                else if (!ServerScript.Instance.AllowedLevels.Contains(messageParts[1]))
                                    LogChat(Network.player,
                                            "Level " + messageParts[1] + " does not exist. " +
                                            StringHelper.DeepToString(ServerScript.Instance.AllowedLevels), true, true);
                                else
                                {
                                    RoundScript.Instance.networkView.RPC("ChangeLevelTo", RPCMode.All,
                                                                            messageParts[1]);
                                    RoundScript.Instance.networkView.RPC("RestartRound", RPCMode.All);
                                }
                                break;

                            case "/spectate":
                                if (!ServerScript.Spectating)
                                {
                                    bool isDead = false;
                                    foreach (var p in PlayerScript.AllEnabledPlayerScripts)
                                        if (p.networkView != null && p.networkView.isMine)
                                        {
                                            var h = p.GetComponent<HealthScript>();
                                            if (h.Health == 0)
                                            {
                                                LogChat(Network.player, "Wait until you respawned to spectate", true, true);
                                                isDead = true;
                                                break;
                                            }

                                            p.GetComponent<HealthScript>().networkView.RPC("ToggleSpectate",
                                                                                           RPCMode.All, true);
                                        }

                                    if (!isDead)
                                    {
                                        ServerScript.Spectating = true;
                                        networkView.RPC("LogChat", RPCMode.All, Network.player,
                                                        "went in spectator mode.", true, false);
                                    }
                                }
                                else
                                    LogChat(Network.player, "Already spectating!", true, true);
                                break;

                            case "/join":
                                if (ServerScript.Spectating)
                                {
                                    foreach (var p in PlayerScript.AllEnabledPlayerScripts)
                                        if (p.networkView != null && p.networkView.isMine)
                                            p.GetComponent<HealthScript>().Respawn(RespawnZone.GetRespawnPoint());

                                    networkView.RPC("LogChat", RPCMode.All, Network.player, "rejoined the game.",
                                                    true, false);

                                    ServerScript.Spectating = false;
                                }
                                else
                                    LogChat(Network.player, "Already in-game!", true, true);
                                break;

                            case "/connect":
                                if (messageParts.Length != 2)
                                {
                                    LogChat(Network.player, "Expected usage : /join 123.23.45.2", true, true);
                                    break;
                                }

                                TaskManager.Instance.WaitFor(0.1f).Then(() =>
                                {
                                    GlobalSoundsScript.PlayButtonPress();
                                    Network.Disconnect();
                                    ServerScript.Spectating = false;
                                    TaskManager.Instance.WaitFor(0.75f).Then(
                                        () => Network.Connect(messageParts[1], ServerScript.Port));
                                });
                                break;

                            case "/endround":
                                if (Network.isServer)
                                    RoundScript.Instance.EndRoundSoon();
                                break;

                            default:
                                LogChat(Network.player, lastMessage + " command not recognized.", true, true);
                                break;
                        }
                    }
                    else
                    {
                        if (lastMessage.Trim() != string.Empty)
                            networkView.RPC("LogChat", RPCMode.All, Network.player, lastMessage, false, false);
                    }

                    lastMessage = string.Empty;
                    showChat = false;
                }

                if (Event.current.keyCode == KeyCode.Escape)
                {
                    lastMessage = string.Empty;
                    showChat = false;
                    Screen.lockCursor = true;
                }

                GUI.FocusControl("ChatInput");
            }
			
			GUILayout.Box( "", new GUIStyle( Skin.box ) { fixedWidth = showChat ? 1 : 184 } );
			
            // TODO was unused. intentional?
			//GUIStyle bsty = new GUIStyle( Skin.button ) { fixedWidth = 92 };

            if (GUILayout.Button("Disconnect"))
            {
                GlobalSoundsScript.PlayButtonPress();
                Network.Disconnect();
                ServerScript.Spectating = false;
            }

            GUILayout.EndHorizontal();
        }
        catch (ArgumentException)
        {
            // Wtf...
        }
    }
	
	[RPC]
    public void LogChat(NetworkPlayer player, string message, bool systemMessage, bool isSourceless)
    {
        //ChatLog.Insert(0, new Pair<string, string>(username, message));
        ChatLog.Add(new ChatMessage { Player = player, Message = message, IsSystem = systemMessage, IsSourceless = isSourceless });
        if (ChatLog.Count > 60)
            ChatLog.RemoveAt(0);
    }

    public class ChatMessage
    {
        public NetworkPlayer Player;
        public string Message;
        public bool IsSystem, IsSourceless;
        public float Life;
        public bool Hidden;
    }
}