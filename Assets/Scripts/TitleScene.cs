using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TitleScene : MonoBehaviour
{
    public void OpenEditor()
    {
        if (GameSceneManager.Instance != null)
        {
            GameSceneManager.Instance.LoadScene("Editor");
        }
    }
}
