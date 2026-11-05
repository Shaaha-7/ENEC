using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Qandeel.EditorTools
{
    /// <summary>Batch-mode setup: adds HDRP (the version that matches this editor) and the local physics core package.
    /// Unity.exe -batchmode -quit -projectPath ... -executeMethod Qandeel.EditorTools.Bootstrap.AddPackages</summary>
    public static class Bootstrap
    {
        public static void AddPackages()
        {
            Add("com.unity.render-pipelines.high-definition");
            Add("file:../../../core");
            AssetDatabase.Refresh();
        }

        static void Add(string id)
        {
            var req = Client.Add(id);
            while (!req.IsCompleted) Thread.Sleep(200);
            if (req.Status == StatusCode.Success) Debug.Log($"[Qandeel] added {req.Result.name} {req.Result.version}");
            else { Debug.LogError($"[Qandeel] could not add {id}: {req.Error?.message}"); EditorApplication.Exit(2); }
        }
    }
}
