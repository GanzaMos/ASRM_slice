using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Katana : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        //Debug.Log("cut");
        GameManager.Instance.Cut(other.gameObject);

        if (other.gameObject.tag == "Table")
        {
            //Debug.Log("Table");
            GameManager.Instance.OnTableTouch();
        }
    }
}
