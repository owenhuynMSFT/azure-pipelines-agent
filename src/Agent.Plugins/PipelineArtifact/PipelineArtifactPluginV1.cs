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

        public Task RunAsync(AgentTaskPluginExecutionContext context, CancellationToken token)
        {
            return this.ProcessCommandInternalAsync(context, token);
        }

        // Process the command with preprocessed arguments.
        protected abstract Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context, 
            CancellationToken token);

        // Properties set by tasks
        protected static class ArtifactEventProperties
        {
            //TODO: place all component IDs in here
            public static readonly string BuildType = "buildType";
            public static readonly string Project = "project";
            public static readonly string BuildPipelineDefinition = "definition";
            public static readonly string BuildVersionToDownload = "buildVersionToDownload";
            public static readonly string BranchName = "branchName";
            public static readonly string BuildId = "buildId";
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

        protected override Task ProcessCommandInternalAsync(
            AgentTaskPluginExecutionContext context, 
            CancellationToken token)
        {
            throw new NotImplementedException();

            // TODO: parse out components from context

            
            // TODO: grab build information


            // TODO: call download given build information
        }
    }
}