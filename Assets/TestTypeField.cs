using UnityEngine;


public class TestTypeField : MonoBehaviour
{
    [Space, Header("---> Single with label")]
    [TypeField(typeof(UnityEngine.Object))]
    public string SingleTypeField_01 = default;

    [Space, Header("---> Array with element labels")]
    [TypeField(typeof(UnityEngine.Object))]
    public string[] ArrayTypeField_01 = default;

    [Space, Header("---> Single without label")]
    [TypeField(typeof(UnityEngine.Object), true)]
    public string SingleTypeField_02 = default;

    [Space, Header("---> Array without element labels")]
    [TypeField(typeof(UnityEngine.Object), true)]
    public string[] ArrayTypeField_02 = default;
}