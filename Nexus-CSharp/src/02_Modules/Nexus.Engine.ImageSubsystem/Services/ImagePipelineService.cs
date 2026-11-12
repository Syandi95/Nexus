using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Engine.ImageSubsystem.Services
{
    public enum ImagePipelineLifecycleState
    {
        Idle,
        ImageLoaded,
        ModelLoading,
        ModelLoaded,
        Processing,
        Result,
        Failed,
        Disposed
    }

    public interface IImagePipelineService : IDisposable
    {
        string CreatePipeline(string root);
        void RegisterTask(string taskId, string task);
        void SetInput(string path);
        bool IsSamePath(string left, string right);
        void ClearResult();
        void TransitionState(ImagePipelineLifecycleState newState);
        ImagePipelineLifecycleState CurrentState { get; }
        SemaphoreSlim PipelineLock { get; }
    }

    public class ImagePipelineService : IImagePipelineService
    {
        private string? _pipelineRoot;
        private readonly Dictionary<string, string> _tasks = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private bool _isDisposed;

        public ImagePipelineLifecycleState CurrentState { get; private set; } = ImagePipelineLifecycleState.Idle;
        public SemaphoreSlim PipelineLock => _semaphore;

        public string CreatePipeline(string root)
        {
            ThrowIfDisposed();
            _pipelineRoot = root;
            TransitionState(ImagePipelineLifecycleState.Idle);
            return root;
        }

        public void RegisterTask(string taskId, string task)
        {
            ThrowIfDisposed();
            _tasks[taskId] = task;
        }

        public void SetInput(string path)
        {
            ThrowIfDisposed();
            TransitionState(ImagePipelineLifecycleState.ImageLoaded);
        }

        public bool IsSamePath(string left, string right)
        {
            if (left == null || right == null) return false;
            return Path.GetFullPath(left).Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }

        public void ClearResult()
        {
            ThrowIfDisposed();
            // Clear temporary results
        }

        public void TransitionState(ImagePipelineLifecycleState newState)
        {
            ThrowIfDisposed();
            ValidateStateTransition(CurrentState, newState);
            CurrentState = newState;
        }

        private static void ValidateStateTransition(ImagePipelineLifecycleState current, ImagePipelineLifecycleState target)
        {
            if (current == target) return; // Same state retention is valid
            if (target == ImagePipelineLifecycleState.Failed || target == ImagePipelineLifecycleState.Disposed)
            {
                return; // Emergency / cleanup transitions are always allowed
            }

            bool isValid = (current, target) switch
            {
                (ImagePipelineLifecycleState.Idle, ImagePipelineLifecycleState.ImageLoaded) => true,
                (ImagePipelineLifecycleState.ImageLoaded, ImagePipelineLifecycleState.ModelLoading) => true,
                (ImagePipelineLifecycleState.ModelLoading, ImagePipelineLifecycleState.ModelLoaded) => true,
                (ImagePipelineLifecycleState.ModelLoaded, ImagePipelineLifecycleState.Processing) => true,
                (ImagePipelineLifecycleState.Processing, ImagePipelineLifecycleState.Result) => true,
                (ImagePipelineLifecycleState.Result, ImagePipelineLifecycleState.ImageLoaded) => true,
                (ImagePipelineLifecycleState.Result, ImagePipelineLifecycleState.Idle) => true,
                (ImagePipelineLifecycleState.Failed, ImagePipelineLifecycleState.ImageLoaded) => true,
                (ImagePipelineLifecycleState.Failed, ImagePipelineLifecycleState.Idle) => true,
                _ => false
            };

            if (!isValid)
            {
                throw new InvalidOperationException($"Invalid image pipeline state transition from {current} to {target}.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(ImagePipelineService));
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                CurrentState = ImagePipelineLifecycleState.Disposed;
                _semaphore.Dispose();
                _isDisposed = true;
            }
        }
    }
}
