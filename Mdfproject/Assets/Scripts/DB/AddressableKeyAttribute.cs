using UnityEngine;

// 이 Attribute는 string 필드에 붙여서, 
// 어떤 타입의 어드레서블 에셋을 참조하는지 알려주는 역할을 합니다.
public class AddressableKeyAttribute : PropertyAttribute
{
    public System.Type AssetType;

    public AddressableKeyAttribute(System.Type type)
    {
        this.AssetType = type;
    }
}