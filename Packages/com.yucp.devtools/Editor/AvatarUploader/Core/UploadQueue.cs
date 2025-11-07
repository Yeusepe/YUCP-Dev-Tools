using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YUCP.DevTools.Editor.AvatarUploader
{
	/// <summary>
	/// Manages a queue of avatar upload operations.
	/// </summary>
	public class UploadQueue
	{
		private readonly Queue<UploadQueueItem> _queue = new Queue<UploadQueueItem>();
		private readonly List<UploadQueueItem> _history = new List<UploadQueueItem>();
		private UploadQueueItem _currentItem;
		private bool _isProcessing;

		public event Action<UploadQueueItem> OnItemAdded;
		public event Action<UploadQueueItem> OnItemStarted;
		public event Action<UploadQueueItem> OnItemCompleted;
		public event Action<UploadQueueItem, string> OnItemFailed;
		public event Action OnQueueCompleted;

		public int QueueCount => _queue.Count;
		public int HistoryCount => _history.Count;
		public bool IsProcessing => _isProcessing;
		public UploadQueueItem CurrentItem => _currentItem;

		public void Enqueue(AvatarUploadProfile profile, AvatarBuildConfig config, string platform, string buildPath)
		{
			var item = new UploadQueueItem
			{
				id = Guid.NewGuid().ToString(),
				profile = profile,
				config = config,
				platform = platform,
				buildPath = buildPath,
				status = UploadStatus.Pending,
				enqueueTime = DateTime.Now
			};

			_queue.Enqueue(item);
			OnItemAdded?.Invoke(item);
		}

		public void StartProcessing()
		{
			if (_isProcessing) return;
			_isProcessing = true;
			ProcessNext();
		}

		public void StopProcessing()
		{
			_isProcessing = false;
		}

		public void ClearQueue()
		{
			_queue.Clear();
		}

		public void RetryFailed()
		{
			var failedItems = _history.Where(i => i.status == UploadStatus.Failed).ToList();
			foreach (var item in failedItems)
			{
				item.status = UploadStatus.Pending;
				item.errorMessage = null;
				_queue.Enqueue(item);
			}
		}

		public List<UploadQueueItem> GetHistory(int maxCount = 100)
		{
			return _history.OrderByDescending(i => i.enqueueTime).Take(maxCount).ToList();
		}

		private void ProcessNext()
		{
			if (!_isProcessing || _queue.Count == 0)
			{
				_isProcessing = false;
				OnQueueCompleted?.Invoke();
				return;
			}

			_currentItem = _queue.Dequeue();
			_currentItem.status = UploadStatus.Uploading;
			_currentItem.startTime = DateTime.Now;
			OnItemStarted?.Invoke(_currentItem);

			// Upload will be handled by AvatarUploader
			// This is just the queue management
		}

		public void MarkCompleted(string itemId)
		{
			if (_currentItem != null && _currentItem.id == itemId)
			{
				_currentItem.status = UploadStatus.Completed;
				_currentItem.completeTime = DateTime.Now;
				_history.Add(_currentItem);
				OnItemCompleted?.Invoke(_currentItem);
				_currentItem = null;
				ProcessNext();
			}
		}

		public void MarkFailed(string itemId, string errorMessage)
		{
			if (_currentItem != null && _currentItem.id == itemId)
			{
				_currentItem.status = UploadStatus.Failed;
				_currentItem.errorMessage = errorMessage;
				_currentItem.completeTime = DateTime.Now;
				_history.Add(_currentItem);
				OnItemFailed?.Invoke(_currentItem, errorMessage);
				_currentItem = null;
				ProcessNext();
			}
		}
	}

	[Serializable]
	public class UploadQueueItem
	{
		public string id;
		public AvatarUploadProfile profile;
		public AvatarBuildConfig config;
		public string platform;
		public string buildPath;
		public UploadStatus status;
		public string errorMessage;
		public DateTime enqueueTime;
		public DateTime? startTime;
		public DateTime? completeTime;

		public float Progress { get; set; }
		public string StatusMessage { get; set; }
	}

	public enum UploadStatus
	{
		Pending,
		Uploading,
		Completed,
		Failed,
		Cancelled
	}
}

