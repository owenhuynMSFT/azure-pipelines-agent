using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.Content.Common.Tracing;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;
using Agent.Sdk;

namespace Agent.Plugins.PipelineArtifact
{
    public abstract class PipelineArtifactTaskPluginBaseV1 : IAgentTaskPlugin
    {
        public abstract Guid Id { get; }
        public string Version => "1.0.0"; // Publish and Download tasks will be always on the same version.
        public string Stage => "main";

        public async Task RunAsync(AgentTaskPluginExecutionContext context, CancellationToken token)
        {
            await this.ProcessCommandInternalAsync(context, token);
        }

        // Process the command with preprocessed arguments.
        protected abstract Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context, 
            CancellationToken token);

        // Properties set by tasks
        protected static class ArtifactEventProperties
        {
            public static readonly string BuildType = "buildType";
            public static readonly string Project = "project";
            public static readonly string BuildPipelineDefinition = "definition";
            public static readonly string BuildTriggering = "specificBuildWithTriggering";
            public static readonly string BuildVersionToDownload = "buildVersionToDownload";
            public static readonly string BranchName = "branchName";
            public static readonly string BuildId = "buildId";
            public static readonly string Tags = "tags";
            public static readonly string ArtifactName = "artifactName";
            public static readonly string ItemPattern = "itemPattern";
            public static readonly string DownloadPath = "downloadPath";
        }
    }

    // Caller: DownloadPipelineArtifact task
    // Can be invoked from a build run or a release run should a build be set as the artifact. 
    public class DownloadPipelineArtifactTaskV1 : PipelineArtifactTaskPluginBaseV1
    {
        // Same as https://github.com/Microsoft/vsts-tasks/blob/master/Tasks/DownloadPipelineArtifactV1/task.json
        public override Guid Id => PipelineArtifactPluginConstants.DownloadPipelineArtifactTaskId;

        protected override async Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context, 
            CancellationToken token)
        {
            ArgUtil.NotNull(context, nameof(context));
            string buildType = context.GetInput(ArtifactEventProperties.BuildType, required: true);
            string project = context.GetInput(ArtifactEventProperties.Project, required: false);
            string buildPipelineDefinition = context.GetInput(ArtifactEventProperties.BuildPipelineDefinition, required: false);
            string buildTriggering = context.GetInput(ArtifactEventProperties.BuildTriggering, required: false);
            string buildVersionToDownload = context.GetInput(ArtifactEventProperties.BuildVersionToDownload, required: false);
            string branchName = context.GetInput(ArtifactEventProperties.BranchName, required: false);
            string buildId = context.GetInput(ArtifactEventProperties.BuildId, required: false);
            string tags = context.GetInput(ArtifactEventProperties.Tags, required: false);
            string artifactName = context.GetInput(ArtifactEventProperties.ArtifactName, required: true);
            string itemPattern = context.GetInput(ArtifactEventProperties.ItemPattern, required: false);
            string downloadPath = context.GetInput(ArtifactEventProperties.DownloadPath, required: true);
            string buildIdStr = context.Variables.GetValueOrDefault(BuildVariables.BuildId)?.Value ?? string.Empty; // BuildID provided my environment.
            string guidStr = string.Empty;

            // Minimatch patterns.
            string[] minimatchPatterns = itemPattern.Split(
                new[] { "\n" },
                StringSplitOptions.None
            );

            //Tags.
            string[] tagsInput = tags.Split(
                new[] { "," },
                StringSplitOptions.None
            );

            if (buildType == "current")
            {
                // Project ID
                // TODO: use a constant for project id, which is currently defined in Microsoft.VisualStudio.Services.Agent.Constants.Variables.System.TeamProjectId (Ting)
                guidStr = context.Variables.GetValueOrDefault("system.teamProjectId")?.Value;
                Guid.TryParse(guidStr, out Guid projectId);
                ArgUtil.NotEmpty(projectId, nameof(projectId));

                int buildIdInt = buildId != ""  ?  Int32.Parse(buildId) : 0;
                //Get the build ID.
                if (int.TryParse(buildIdStr, out buildIdInt) && buildIdInt != 0)
                {
                    context.Output(StringUtil.Loc("DownloadingFromBuild", buildIdInt));
                }
                else
                {
                    string hostType = context.Variables.GetValueOrDefault("system.hosttype")?.Value;
                    if (string.Equals(hostType, "Release", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(hostType, "DeploymentGroup", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(StringUtil.Loc("BuildIdIsNotAvailable", hostType ?? string.Empty));
                    }
                    else if (!string.Equals(hostType, "Build", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(StringUtil.Loc("CannotDownloadFromCurrentEnvironment", hostType ?? string.Empty));
                    }
                    else
                    {
                        // This should not happen since the build id comes from build environment. But a user may override that so we must be careful.
                        throw new ArgumentException(StringUtil.Loc("BuildIdIsNotValid", buildIdStr));
                    }
                } 
                //Create the directory if absent.
                string fullPath = Path.GetFullPath(downloadPath);
                bool isDir = Directory.Exists(fullPath);
                if (!isDir)
                {
                    Directory.CreateDirectory(fullPath);
                }

                context.Output(StringUtil.Loc("DownloadArtifactTo", downloadPath));
                PipelineArtifactServer server = new PipelineArtifactServer();
                await server.DownloadAsyncMinimatch(context, projectId, buildIdInt, artifactName, downloadPath, minimatchPatterns, token);
                context.Output(StringUtil.Loc("DownloadArtifactFinished"));
            }
            else if(buildType == "specific")
            {
                // Project Name - coming from the GUI.
                int buildIdInt = 0;
                if(buildVersionToDownload == "latest")
                {
                    List<int> defns= new List<int>();
                    defns.Add(Int32.Parse(buildPipelineDefinition));
                    VssConnection connection = context.VssConnection;
                    BuildHttpClient _buildHttpClient = connection.GetClient<BuildHttpClient>();
                    List<Build> list = await _buildHttpClient.GetBuildsAsync(project, definitions: defns, tagFilters: tagsInput,  queryOrder: BuildQueryOrder.FinishTimeDescending);
                    if (list.Any())
                    {
                        buildIdInt = list.First().Id;
                    }       
                }
                else if(buildVersionToDownload == "specific")
                {
                    buildIdInt = Int32.Parse(buildId);
                }
                else if(buildVersionToDownload == "latestFromBranch")
                {
                    List<int> defns = new List<int>();
                    defns.Add(Int32.Parse(buildPipelineDefinition));
                    VssConnection connection = context.VssConnection;
                    BuildHttpClient _buildHttpClient = connection.GetClient<BuildHttpClient>();
                    List<Build> list = await _buildHttpClient.GetBuildsAsync(project, definitions: defns, branchName: branchName, tagFilters: tagsInput, queryOrder: BuildQueryOrder.FinishTimeDescending);
                    if (list.Any())
                    {
                        buildIdInt = list.First().Id;
                    }
                }
                //Create the directory if absent.
                string fullPath = Path.GetFullPath(downloadPath);
                bool isDir = Directory.Exists(fullPath);
                if (!isDir)
                {
                    Directory.CreateDirectory(fullPath);
                }

                context.Output(StringUtil.Loc("DownloadArtifactTo", downloadPath));
                PipelineArtifactServer server = new PipelineArtifactServer();
                await server.DownloadAsyncWithProjectNameMiniMatch(context, project, buildIdInt, artifactName, downloadPath, minimatchPatterns, token);
                context.Output(StringUtil.Loc("DownloadArtifactFinished"));
            }
        }
    }
}