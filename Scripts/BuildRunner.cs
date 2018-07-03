using UnityEditor;

namespace UnityLauncher
{
    public class BuildRunner
    {
        public Run()
        {
            var buildPlayerOptions = new BuildPlayerOptions();
            buildPlayerOptions.scenes = EditorBuildSettingsScene.GetActiveSceneList(EditorBuildSettings.scenes);

            buildPlayerOptions.locationPathName = p[0];
            buildPlayerOptions.assetBundleManifestPath = p[1];

            buildPlayerOptions.targetGroup = BuildPipeline.GetBuildTargetGroup(t);

            buildPlayerOptions.target = t;
            buildPlayerOptions.options = o;

            BuildPipeline.BuildPlayer(buildPlayerOptions);
        }
    }
}