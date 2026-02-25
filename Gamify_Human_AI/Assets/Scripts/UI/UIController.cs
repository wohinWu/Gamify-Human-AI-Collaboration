using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIController : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void OnClickStartButton()
    {
        SceneFlowManager.Instance.LoadStartScene();
    }

    public void OnClickJournalButton()
    {
        SceneFlowManager.Instance.LoadJournalScene();
    }

    public void OnClickWalkingButton()
    {
        SceneFlowManager.Instance.LoadWalkingScene();
    }

    public void OnClickMapButton()
    {
        SceneFlowManager.Instance.LoadMapScene();
    }
}
