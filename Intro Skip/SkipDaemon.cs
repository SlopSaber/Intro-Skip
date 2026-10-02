using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SiraUtil.Logging;
using UnityEngine;
using UnityEngine.XR;
using Zenject;

namespace IntroSkip
{
    internal class SkipDaemon : IInitializable, ITickable, IDisposable
    {
        private readonly Config _config;
        private readonly SiraLog _siraLog;
        private readonly IVRPlatformHelper _vrPlatformHelper;
        private readonly ISkipDisplayService _skipDisplayService;
        private AudioTimeSyncController _audioTimeSyncController;
        private readonly IReadonlyBeatmapData _readonlyBeatmapData;
        private readonly AudioTimeSyncController.InitData _initData;
        private Task<TimingBounds>? _preparation;
        private CancellationTokenSource? _preparationCancellation;
        private LinkedList<BeatmapDataItem>? _preparedItems;
        private LinkedList<BeatmapDataItem>.Enumerator _preparedVersion;
        private int _preparedCount;
        private AudioClip? _preparedClip;
        private AudioTimeSyncController? _preparedController;
        private float _preparedAudioLength;
        private bool _disposed;

        private readonly struct TimingSample
        {
            internal readonly byte Kind;
            internal readonly float Time;
            internal readonly float X;
            internal readonly float Y;
            internal readonly float Width;
            internal readonly float Height;

            internal TimingSample(byte kind, float time, float x = 0f, float y = 0f,
                float width = 0f, float height = 0f)
            {
                Kind = kind;
                Time = time;
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }
        }

        private readonly struct TimingBounds
        {
            internal readonly int Count;
            internal readonly float First;
            internal readonly float Last;
            internal readonly bool HasIntro;
            internal readonly bool HasOutro;
            internal readonly float IntroTime;
            internal readonly float OutroTime;
            internal readonly float LastObjectTime;

            internal TimingBounds(int count, float first, float last, float audioLength)
            {
                Count = count;
                First = first;
                Last = last;
                HasIntro = count != 0 && first > 5f;
                HasOutro = count != 0 && audioLength - last >= 5f;
                IntroTime = HasIntro ? first - 2f : -1f;
                OutroTime = HasOutro ? audioLength - 1.5f : -1f;
                LastObjectTime = HasOutro ? last + 0.5f : -1f;
            }
        }

        private float _introSkipTime = -1f;
        private float _outroSkipTime = -1f;
        private bool _skippableOutro = false;
        private bool _skippableIntro = false;
        private float _lastObjectSkipTime = -1f;

        public bool CanSkip => InIntroPhase || InOutroPhase;
        public bool InIntroPhase => _audioTimeSyncController.songTime < _introSkipTime && _skippableIntro;
        public bool InOutroPhase => _audioTimeSyncController.songTime > _lastObjectSkipTime && _audioTimeSyncController.songTime < _outroSkipTime && _skippableOutro;
        public bool WantsToSkip => _audioTimeSyncController.state == IAudioTimeSource.State.Playing && (_vrPlatformHelper.GetTriggerValue(XRNode.LeftHand) >= .8 || _vrPlatformHelper.GetTriggerValue(XRNode.RightHand) >= .8 || Input.GetKey(KeyCode.I));

        public SkipDaemon(Config config, SiraLog siraLog, IVRPlatformHelper vrPlatformHelper, ISkipDisplayService skipDisplayService, AudioTimeSyncController audioTimeSyncController, IReadonlyBeatmapData readonlyBeatmapData, AudioTimeSyncController.InitData initData)
        {
            _config = config;
            _siraLog = siraLog;
            _initData = initData;
            _vrPlatformHelper = vrPlatformHelper;
            _skipDisplayService = skipDisplayService;
            _readonlyBeatmapData = readonlyBeatmapData;
            _audioTimeSyncController = audioTimeSyncController;
            StartPreparation();
        }

        public void Initialize()
        {
            if (_disposed) return;
            _skippableIntro = false;
            _skippableOutro = false;
            _introSkipTime = -1;
            _outroSkipTime = -1;
            _lastObjectSkipTime = -1;

            if (_preparation == null || !PreparationIsCurrent()) StartPreparation();
            PublishCompletedPreparation();
        }

        private bool PreparationIsCurrent()
        {
            AudioTimeSyncController? controller = _preparedController;
            AudioClip? clip = _preparedClip;
            LinkedList<BeatmapDataItem>? items = _preparedItems;
            if (controller is null || !controller || controller != _audioTimeSyncController ||
                clip is null || !clip || clip != _initData.audioClip || items is null ||
                !_preparedAudioLength.Equals(_initData.audioClip.length) ||
                !ReferenceEquals(items, _readonlyBeatmapData.allBeatmapDataItems) ||
                items.Count != _preparedCount) return false;
            try
            {
                var version = _preparedVersion;
                version.MoveNext();
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void StartPreparation()
        {
            Task<TimingBounds>? previous = _preparation;
            CancellationTokenSource? previousCancellation = _preparationCancellation;
            previousCancellation?.Cancel();
            _ = RetirePreparationAsync(previous, previousCancellation);

            _preparedItems = _readonlyBeatmapData.allBeatmapDataItems;
            _preparedVersion = _preparedItems.GetEnumerator();
            _preparedCount = _preparedItems.Count;
            _preparedClip = _initData.audioClip;
            _preparedController = _audioTimeSyncController;
            _preparedAudioLength = _preparedClip.length;
            var samples = new TimingSample[_preparedCount];
            int index = 0;
            foreach (var item in _preparedItems)
            {
                if (item is NoteData)
                    samples[index] = new TimingSample(1, item.time);
                else if (item is ObstacleData obstacle)
                    samples[index] = new TimingSample(2, item.time, obstacle.lineIndex,
                        (int)obstacle.lineLayer, obstacle.width, obstacle.height);
                index++;
            }
            _preparationCancellation = new CancellationTokenSource();
            _preparation = PrepareTimingAsync(samples, _preparedAudioLength,
                _preparationCancellation.Token, previous);
        }

        private static async Task<TimingBounds> PrepareTimingAsync(TimingSample[] samples,
            float audioLength, CancellationToken cancellation, Task<TimingBounds>? previous)
        {
            if (previous != null)
            {
                try { await previous.ConfigureAwait(false); }
                catch (Exception) { }
            }
            cancellation.ThrowIfCancellationRequested();
            return await Task.Run(() => CalculateTiming(samples, audioLength, cancellation),
                cancellation).ConfigureAwait(false);
        }

        private static TimingBounds CalculateTiming(TimingSample[] samples, float audioLength,
            CancellationToken cancellation)
        {
            float first = audioLength;
            float last = -1f;
            int count = 0;
            foreach (TimingSample sample in samples)
            {
                cancellation.ThrowIfCancellationRequested();
                if (sample.Kind == 1 || (sample.Kind == 2 && sample.X + sample.Width > 1f &&
                    sample.X < 3f && sample.Y + sample.Height > 1f && sample.Y < 3f))
                {
                    count++;
                    if (sample.Time < first) first = sample.Time;
                    if (sample.Time > last) last = sample.Time;
                }
            }
            return new TimingBounds(count, first, last, audioLength);
        }

        private void PublishCompletedPreparation()
        {
            Task<TimingBounds>? preparation = _preparation;
            if (_disposed || preparation == null || !preparation.IsCompleted) return;
            if (!_audioTimeSyncController || !_initData.audioClip)
            {
                Dispose();
                return;
            }
            if (!PreparationIsCurrent())
            {
                StartPreparation();
                return;
            }
            _preparation = null;
            try
            {
                TimingBounds bounds = preparation.GetAwaiter().GetResult();
                if (bounds.Count == 0) return;
                _skippableIntro = bounds.HasIntro && _config.AllowIntroSkip;
                _skippableOutro = bounds.HasOutro && _config.AllowOutroSkip;
                _introSkipTime = bounds.IntroTime;
                _outroSkipTime = bounds.OutroTime;
                _lastObjectSkipTime = bounds.LastObjectTime;
                _siraLog.Debug($"Skippable Intro: {_skippableIntro} | Skippable Outro: {_skippableOutro}");
                _siraLog.Debug($"First Object Time: {bounds.First} | Last Object Time: {bounds.Last}");
                _siraLog.Debug($"Intro Skip Time: {_introSkipTime} | Outro Skip Time: {_outroSkipTime}");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _siraLog.Error($"Unable to prepare skip timing: {ex}"); }
            finally
            {
                _preparationCancellation?.Dispose();
                _preparationCancellation = null;
            }
        }

        public void Tick()
        {
            PublishCompletedPreparation();
            if (_disposed) return;
            if (CanSkip)
            {
                if (!_skipDisplayService.Active)
                    _skipDisplayService.Show();

                if (WantsToSkip)
                {
                    _vrPlatformHelper.TriggerHapticPulse(XRNode.LeftHand, 0.1f, 0.2f, 1);
                    _vrPlatformHelper.TriggerHapticPulse(XRNode.RightHand, 0.1f, 0.2f, 1);
                    if (InIntroPhase)
                    {
                        _audioTimeSyncController.SeekTo((_introSkipTime - _audioTimeSyncController.startSongTime) / _audioTimeSyncController.timeScale);
                        _skippableIntro = false;
                    }
                    else if (InOutroPhase)
                    {
                        _audioTimeSyncController.SeekTo((_outroSkipTime - _audioTimeSyncController.startSongTime) / _audioTimeSyncController.timeScale);
                        _skippableOutro = false;
                    }
                }
            }
            else if (_skipDisplayService.Active && !CanSkip)
            {
                _skipDisplayService.Hide();
                return;
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _preparationCancellation?.Cancel();
            _ = RetirePreparationAsync(_preparation, _preparationCancellation);
            _preparation = null;
            _preparationCancellation = null;
        }

        private static async Task RetirePreparationAsync(Task<TimingBounds>? preparation,
            CancellationTokenSource? cancellation)
        {
            if (preparation != null)
            {
                try { await preparation.ConfigureAwait(false); }
                catch (Exception) { }
            }
            cancellation?.Dispose();
        }
    }
}
