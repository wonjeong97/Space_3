using System.Threading.Tasks;
using UnityEngine;

public class RocketAnimEvent : MonoBehaviour
{
    public async Task CallNextScene()
    {
        if (LaunchManager.Instance)
        {
            await LaunchManager.Instance.LoadNextSceneAsync();
        }
    }
}
