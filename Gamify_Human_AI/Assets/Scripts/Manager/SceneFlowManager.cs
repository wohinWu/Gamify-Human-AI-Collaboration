using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneFlowManager : MonoSingleton<SceneFlowManager>
{
    protected override void Awake()
    {
        base.Awake();
    }

    void Start()
    {
        
    }

    void Update()
    {
        
    }

    public void LoadStartScene()
    {
        SceneManager.LoadScene("StartScene");
    }

    public void LoadMapScene()
    {
        SceneManager.LoadScene("MapScene");
    }

    public void LoadJournalScene()
    {
        SceneManager.LoadScene("JournalScene");
    }

    public void LoadWalkingScene()
    {
        SceneManager.LoadScene("WalkingScene");
    }
}
