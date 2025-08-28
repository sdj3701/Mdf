using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using TMPro;

public class NetworkManager : MonoBehaviourPunCallbacks
{
    public static NetworkManager Instance { get; private set; }

    // UI로 서버 상태 확인 추후 UIMananger로 변경
    public TMP_Text StatusText;

    // UI로 닉네임, 패스워드 입력
    // TODO : DB로 변경
    public TMP_InputField NickNameInput, PassWordInput, RoomInput;

    public GameObject RoomList;
    public GameObject Title;


    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }

        // Photon 서버 접속 설정
        Screen.SetResolution(960, 540, false);
    }

    void Start()
    {
        
    }

    void Update()
    {
        StatusText.text = PhotonNetwork.NetworkClientState.ToString();
    }

    #region 서버 접속
    public void Connect() => PhotonNetwork.ConnectUsingSettings();

    public override void OnConnectedToMaster()
    {
        Debug.Log("서버접속완료");
        PhotonNetwork.LocalPlayer.NickName = NickNameInput.text;
        Title.SetActive(false);
        RoomList.SetActive(true);
    }
    #endregion

    #region 서버 접속 실패 및 종료
    // 서버 접속 실패시 강제 종료
    public override void OnDisconnected(DisconnectCause cause)
    {
        Debug.Log($"서버접속실패 : {cause.ToString()}");
/*#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(); // 어플리케이션 종료
#endif*/
    }
    #endregion

    #region 조인 로비
    // 서버 이름 같은 느낌? ex) 메이플 스카니아,베라,크로아 느낌
    public void JoinLobby() => PhotonNetwork.JoinLobby();
    public override void OnJoinedLobby()
    {
        Debug.Log("로비접속완료");
    }

    #endregion


    #region 방 생성
    public void CreateRoom() => PhotonNetwork.CreateRoom(RoomInput.text, new RoomOptions { MaxPlayers = 2 });

    public void JoinRoom() => PhotonNetwork.JoinRoom(RoomInput.text);

    public void JoinOrCreateRoom() => PhotonNetwork.JoinOrCreateRoom(RoomInput.text, new RoomOptions { MaxPlayers = 2 }, null);

    public void JoinRandomRoom() => PhotonNetwork.JoinRandomRoom();

    public void LeaveRoom() => PhotonNetwork.LeaveRoom();

    public override void OnCreatedRoom() => Debug.Log("방만들기완료");

    public override void OnJoinedRoom() => Debug.Log("방참가완료");

    public override void OnCreateRoomFailed(short returnCode, string message) => Debug.Log("방만들기실패");

    public override void OnJoinRoomFailed(short returnCode, string message) => Debug.Log("방참가실패");

    public override void OnJoinRandomFailed(short returnCode, string message) => Debug.Log("방랜덤참가실패");

    #endregion





















    #region
    #endregion

    #region 
    #endregion

    #region 
    #endregion
}
