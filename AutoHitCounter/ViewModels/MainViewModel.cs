//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using AutoHitCounter.Core;
using AutoHitCounter.Enums;
using AutoHitCounter.Interfaces;
using AutoHitCounter.Mappers;
using AutoHitCounter.Models;
using AutoHitCounter.Services;
using AutoHitCounter.Utilities;
using AutoHitCounter.Views.Windows;

namespace AutoHitCounter.ViewModels
{
    public class MainViewModel : BaseViewModel, IReorderHandler, IHitRulesProvider
    {
        private readonly IHotkeyManager _hotkeyManager;
        private readonly IGameModuleFactory _gameModuleFactory;
        private readonly IProfileService _profileService;
        private readonly ISplitNavigationService _splitNav;
        private readonly IOverlayServerService _overlayServerService;
        private readonly ICustomGameService _customGameService;
        private string _lastIgt;
        private readonly IRunStateService _runStateService;
        private readonly IGameSessionOrchestrator _orchestrator;

        public SettingsViewModel Settings { get; }
        public HotkeyTabViewModel Hotkeys { get; }

        public MainViewModel(IHotkeyManager hotkeyManager,
            IGameModuleFactory gameModuleFactory,
            IProfileService profileService, IStateService stateService, SettingsViewModel settings,
            HotkeyTabViewModel hotkeyTabViewModel, IOverlayServerService overlayServerService,
            ISplitNavigationService splitNavigationService, IExternalIntegrationService externalIntegrationService,
            IGameSessionOrchestrator orchestrator,
            IRunStateService runStateService, ICustomGameService customGameService)
        {
            Settings = settings;
            Hotkeys = hotkeyTabViewModel;
            _orchestrator = orchestrator;
            _orchestrator.Initialize(this, GetActiveEvents);
            _hotkeyManager = hotkeyManager;
            _gameModuleFactory = gameModuleFactory;
            _profileService = profileService;
            _overlayServerService = overlayServerService;
            _overlayServerService.Start();

            stateService.Subscribe(State.AppStart, OnAppStart);

            if (Settings != null)
                Settings.OnGameSettingChanged += () => _orchestrator.ApplyCurrentSettings();

            _orchestrator.AttachmentChanged += OnOrchestratorAttachmentChanged;
            _orchestrator.HitReceived += async () =>
            {
                if (IsRunComplete || CurrentSplit == null || IsPracticeMode) return;
                if (_selectedGame != _orchestrator.ActiveGame) return;
                CurrentSplit.NumOfHits++;
                SaveRunState();
                _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));

                var payload = new HitPayload(_orchestrator.ActiveGame, ActiveProfile, CurrentSplit, TotalHits, TotalPb, InGameTime);
                await externalIntegrationService.SendHitAsync(payload);
            };
            _orchestrator.RunStartDetected += HandleRunStart;
            _orchestrator.EventSetDetected += AutoAdvanceSplit;
            _orchestrator.EventLogEntries += entries => _eventLogViewModel?.RefreshEventLogs(entries);
            _orchestrator.TimeChangedMs += UpdateInGameTime;
            _orchestrator.BossHealthBarSpawnDetected += HandleBossHealthBarSpawn;
            _orchestrator.BossGaugeActivated += HandleBossGaugeActivated;
            _orchestrator.GameUnloaded += HandleGameUnloaded;

            _splitNav = splitNavigationService;
            _splitNav.Load(Splits);
            _splitNav.StateChanged += OnSplitStateChanged;

            _runStateService = runStateService;
            _customGameService = customGameService;

            RegisterHotkeys();
            
            _isUnlocked = SettingsManager.Default.IsUnlocked;

            ThemeService.ThemeChanged += OnThemeChanged;
            InitialiseCommands();
            
            foreach (var game in _gameModuleFactory.GetRegisteredGames())
                Games.Add(game);

            LoadCustomGames();

            SelectedGame = Games.FirstOrDefault(game => game.GameName == SettingsManager.Default.LastSelectedGame);
            if (_selectedGame != null)
                StartTrackingGame();
        }

        #region Commands

        public DelegateCommand CheckUpdateCommand { get; set; }
        public DelegateCommand OpenProfileEditorCommand { get; set; }
        public DelegateCommand OpenEventLogCommand { get; set; }

        public DelegateCommand TrackGameCommand { get; set; }
        public DelegateCommand CreateCustomGameCommand { get; set; }
        public DelegateCommand DeleteCustomGameCommand { get; set; }
        public DelegateCommand RenameCustomGameCommand { get; set; }

        public DelegateCommand ManualSplitCommand { get; set; }
        public DelegateCommand AdvanceSplitCommand { get; set; }
        public DelegateCommand PrevSplitCommand { get; set; }

        public DelegateCommand IncrementHitCommand { get; set; }
        public DelegateCommand DecrementHitCommand { get; set; }

        public DelegateCommand ResetCommand { get; set; }

        public DelegateCommand SetPbCommand { get; set; }

        public DelegateCommand MarkTimesAsPbCommand { get; set; }

        public DelegateCommand ExportRunDataCommand { get; set; }

        public DelegateCommand SaveNotesCommand { get; set; }

        public DelegateCommand ClearAllNotesCommand { get; set; }

        public DelegateCommand ToggleLockCommand { get; set; }

        public DelegateCommand ResetSelectedSplitHitsCommand { get; set; }

        public DelegateCommand ResetSelectedSplitBossTimerCommand { get; set; }

        public DelegateCommand RestoreSelectedSplitBossTimerCommand { get; set; }

        public DelegateCommand MarkCurrentTimeAsPbCommand { get; set; }

        public DelegateCommand RenameSelectedSplitCommand { get; set; }

        public DelegateCommand EditAttemptsCommand { get; set; }

        public DelegateCommand ClearTotalPbCommand { get; set; }

        public DelegateCommand EditSplitPbCommand { get; set; }

        public DelegateCommand EditSplitBossKillTimePbCommand { get; set; }

        public DelegateCommand MoveSplitUpCommand { get; set; }
        public DelegateCommand MoveSplitDownCommand { get; set; }

        public DelegateCommand SetDistancePbCommand { get; set; }

        #endregion

        #region Properties

        private string _appVer;

        public string AppVer
        {
            get => _appVer;
            set => SetProperty(ref _appVer, value);
        }

        private string _attachedText;

        public string AttachedText
        {
            get => _attachedText;
            set => SetProperty(ref _attachedText, value);
        }

        private bool _isAttached;

        public bool IsAttached
        {
            get => _isAttached;
            set => SetProperty(ref _isAttached, value);
        }
        
        private AttachmentStatus _attachmentStatus;

        public AttachmentStatus AttachmentStatus
        {
            get => _attachmentStatus;
            set => SetProperty(ref _attachmentStatus, value);
        }

        public ObservableCollection<Game> Games { get; } = new();

        private Game _selectedGame;

        public Game SelectedGame
        {
            get => _selectedGame;
            set
            {
                if (_selectedGame == value) return;

                if (_activeProfile != null)
                    _runStateService.Save(_selectedGame?.GameName, _activeProfile.Name,
                        _runStateService.Capture(Splits, CurrentSplit, IsRunComplete, InGameTime));

                SetProperty(ref _selectedGame, value);
                _activeProfile = null;

                Profiles.Clear();
                foreach (var p in _profileService.GetProfiles(_selectedGame?.GameName))
                    Profiles.Add(p);

                ActiveProfile = Profiles.FirstOrDefault(p => p.Name == SettingsManager.Default.LastSelectedProfile)
                                ?? Profiles.FirstOrDefault();

                if (_selectedGame?.IsManual == true)
                {
                    _isPracticeMode = false;
                    OnPropertyChanged(nameof(IsPracticeMode));
                    StartTrackingGame();
                }
                else
                {
                    _isPracticeMode = SettingsManager.Default.PracticeMode;
                    OnPropertyChanged(nameof(IsPracticeMode));
                }


                if (!Profiles.Any())
                {
                    _activeProfile = null;
                    Splits.Clear();
                    _splitNav.SetPosition(null, false);
                    OnPropertyChanged(nameof(CurrentSplit));
                    OnPropertyChanged(nameof(IsRunComplete));
                    OnPropertyChanged(nameof(ActiveProfile));
                    OnPropertyChanged(nameof(TotalHits));
                    OnPropertyChanged(nameof(TotalDiff));
                    OnPropertyChanged(nameof(TotalHitsBrush));
                    OnPropertyChanged(nameof(TotalPb));
                    _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
                }
            }
        }

        public Game ActiveGame => _orchestrator.ActiveGame;

        public string TrackingText => _orchestrator.ActiveGame != null
            ? $"Track hits for the currently selected game.\nCurrently Tracking: {_orchestrator.ActiveGame.GameName}"
            : "Not tracking";

        public string TimerLabel => _orchestrator.ActiveGame?.IsManual == true ? "RTA" : "IGT";

        public ObservableCollection<SplitViewModel> Splits { get; } = new();

        private SplitViewModel _selectedSplit;

        public SplitViewModel SelectedSplit
        {
            get => _selectedSplit;
            set
            {
                if (_selectedSplit != null && _selectedSplit.IsEditing)
                    CommitRename(_selectedSplit);

                if (SetProperty(ref _selectedSplit, value))
                {
                    MoveSplitUpCommand?.RaiseCanExecuteChanged();
                    MoveSplitDownCommand?.RaiseCanExecuteChanged();
                    SetDistancePbCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public SplitViewModel CurrentSplit
        {
            get => _splitNav.CurrentSplit;
            set
            {
                _splitNav.SetPosition(value, _splitNav.IsRunComplete);
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentSplitNumber));
            }
        }

        private Profile _activeProfile;

        public Profile ActiveProfile
        {
            get => _activeProfile;
            set
            {
                if (_activeProfile == value) return;

                if (_activeProfile != null)
                    _runStateService.Save(_selectedGame?.GameName, _activeProfile.Name,
                        _runStateService.Capture(Splits, CurrentSplit, IsRunComplete, InGameTime));

                SetProperty(ref _activeProfile, value);

                if (value != null)
                {
                    SettingsManager.Default.LastSelectedProfile = value.Name;
                    SettingsManager.Default.Save();
                }

                LoadProfile(value);
                SetDistancePbCommand?.RaiseCanExecuteChanged();

                if (_orchestrator.ActiveGame == _selectedGame)
                    _orchestrator.UpdateEvents(GetActiveEvents());

                OnHitRulesChanged?.Invoke();
            }
        }

        public ObservableCollection<Profile> Profiles { get; } = new();

        private TimeSpan _inGameTime;

        public TimeSpan InGameTime
        {
            get => _inGameTime;
            set => SetProperty(ref _inGameTime, value);
        }

        private bool _isPracticeMode;

        public bool IsPracticeMode
        {
            get => _isPracticeMode;
            set
            {
                if (!SetProperty(ref _isPracticeMode, value)) return;
                SettingsManager.Default.PracticeMode = value;
                SettingsManager.Default.Save();
            }
        }

        private bool _showNotes;

        public bool ShowNotes
        {
            get => _showNotes;
            set => SetProperty(ref _showNotes, value);
        }

        public bool IsRunComplete
        {
            get => _splitNav.IsRunComplete;
            set
            {
                _splitNav.SetPosition(_splitNav.CurrentSplit, value);
                OnPropertyChanged();
            }
        }

        private string _inGameTimeFormatted;

        public string InGameTimeFormatted
        {
            get => _inGameTimeFormatted;
            set => SetProperty(ref _inGameTimeFormatted, value);
        }

        public int TotalHits => Splits.Where(s => s.Type == SplitType.Child).Sum(s => s.NumOfHits);

        public Brush TotalHitsBrush
        {
            get
            {
                if (TotalPb == 0) return GetBrush("DiffNeutralBrush");
                if (TotalHits < TotalPb) return GetBrush("DiffNegativeBrush");
                if (TotalHits > TotalPb) return GetBrush("DiffPositiveBrush");
                return GetBrush("DiffNeutralBrush");
            }
        }

        public int TotalDiff => Splits.Where(s => s.Type == SplitType.Child).Sum(s => s.Diff);

        public int TotalPb => Splits.Where(s => s.Type == SplitType.Child).Sum(s => s.PersonalBest);

        public Brush TotalPbBrush
        {
            get
            {
                if (TotalDiff > 0) return GetBrush("DiffPositiveBrush");
                if (TotalDiff < 0) return GetBrush("DiffNegativeBrush");
                return GetBrush("DiffNeutralBrush");
            }
        }

        public event Action OnHitRulesChanged;

        public bool GetRule(string key) => _activeProfile != null
                                           && _activeProfile.GameSettings.TryGetValue(key, out var val)
                                           && val;

        public void CommitRename(SplitViewModel split)
        {
            split.IsEditing = false;
            if (ActiveProfile == null) return;
            var index = Splits.IndexOf(split);
            if (index >= 0 && index < ActiveProfile.Splits.Count)
            {
                ActiveProfile.Splits[index].Name = split.Name;
                if (!split.IsParent)
                    ActiveProfile.Splits[index].DisplayName = split.Name;
                _profileService.SaveProfile(ActiveProfile);
            }

            _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
            NotifyProfileSplitsChanged();
        }

        public bool HasSplits => TotalSplitCount > 0;

        private bool _isSplitListScrollbarVisible;

        public bool IsSplitListScrollbarVisible
        {
            get => _isSplitListScrollbarVisible;
            set
            {
                _isSplitListScrollbarVisible = value;
                OnPropertyChanged();
            }
        }

        private bool _isEditingAttempts;

        public bool IsEditingAttempts
        {
            get => _isEditingAttempts;
            set => SetProperty(ref _isEditingAttempts, value);
        }

        private bool _isUnlocked = true;

        public bool IsUnlocked
        {
            get => _isUnlocked;
            set => SetProperty(ref _isUnlocked, value);
        }

        public int AttemptCount => _activeProfile?.AttemptCount ?? 0;

        public int CurrentSplitNumber
        {
            get
            {
                if (CurrentSplit == null) return 0;
                var children = Splits.Where(s => s.Type == SplitType.Child).ToList();
                return children.IndexOf(CurrentSplit) + 1;
            }
        }

        public int TotalSplitCount => Splits.Count(s => s.Type == SplitType.Child);

        #endregion

        #region Public Methods

        public void MoveItem(object draggedItem, int dropIndex)
        {
            if (draggedItem is not SplitViewModel entry) return;
            if (entry.IsParent) return;
            if (dropIndex < 0) return;

            var oldIndex = Splits.IndexOf(entry);
            if (oldIndex < 0 || oldIndex == dropIndex) return;

            var groupStart = oldIndex;
            for (int i = oldIndex - 1; i >= 0; i--)
            {
                if (Splits[i].IsParent)
                {
                    groupStart = i + 1;
                    break;
                }

                if (i == 0) groupStart = 0;
            }

            var groupEnd = Splits.Count - 1;
            for (int i = oldIndex + 1; i < Splits.Count; i++)
            {
                if (Splits[i].IsParent)
                {
                    groupEnd = i - 1;
                    break;
                }
            }

            if (dropIndex < groupStart) dropIndex = groupStart;
            if (dropIndex > groupEnd + 1) dropIndex = groupEnd + 1;

            if (oldIndex == dropIndex) return;

            Splits.RemoveAt(oldIndex);

            if (dropIndex > oldIndex)
                dropIndex--;

            Splits.Insert(dropIndex, entry);

            if (ActiveProfile?.Splits != null && oldIndex < ActiveProfile.Splits.Count)
            {
                var profileEntry = ActiveProfile.Splits[oldIndex];
                ActiveProfile.Splits.RemoveAt(oldIndex);
                ActiveProfile.Splits.Insert(dropIndex, profileEntry);
                _profileService.SaveProfile(ActiveProfile);
            }

            _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
            NotifyProfileSplitsChanged();
        }

        public void CommitAttemptsEdit(string value)
        {
            if (int.TryParse(value, out var count) && count >= 0)
            {
                _activeProfile.AttemptCount = count;
                _profileService.SaveProfile(_activeProfile);
                OnPropertyChanged(nameof(AttemptCount));
                _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
            }

            IsEditingAttempts = false;
        }

        public void JumpToSplit(SplitViewModel target) => _splitNav.JumpTo(target);

        public override void Dispose()
        {
            ThemeService.ThemeChanged -= OnThemeChanged;
        }

        public void SaveRunState() =>
            _runStateService.SaveRunState(_activeProfile, Splits, CurrentSplit, IsRunComplete, InGameTime);

        public void FlushRunState() => _runStateService.FlushRunState(_activeProfile);

        #endregion

        #region Private Methods

        private void OnAppStart()
        {
            AppVer = VersionChecker.GetVersionText();
            if (SettingsManager.Default.EnableUpdateChecks)
                VersionChecker.CheckForUpdates(Application.Current.MainWindow);
            _isPracticeMode = _selectedGame?.IsManual != true && SettingsManager.Default.PracticeMode;
            OnPropertyChanged(nameof(IsPracticeMode));
        }

        private void CheckUpdate() =>
            VersionChecker.CheckForUpdates(Application.Current.MainWindow, true);

        private void InitialiseCommands()
        {
            CheckUpdateCommand = new DelegateCommand(CheckUpdate);
            TrackGameCommand = new DelegateCommand(StartTrackingGame);
            CreateCustomGameCommand = new DelegateCommand(CreateCustomGame);
            DeleteCustomGameCommand = new DelegateCommand(DeleteCustomGame);
            RenameCustomGameCommand = new DelegateCommand(RenameCustomGame);
            OpenProfileEditorCommand = new DelegateCommand(OpenProfileEditor);
            OpenEventLogCommand = new DelegateCommand(OpenEventLog);
            ManualSplitCommand = new DelegateCommand(ManualAdvanceSplit);
            AdvanceSplitCommand = new DelegateCommand(() =>
            {
                ClearBossTimerState();
                _splitNav.Advance();
            });
            PrevSplitCommand = new DelegateCommand(() =>
            {
                ClearBossTimerState();
                _splitNav.Previous();
            });
            IncrementHitCommand = new DelegateCommand(IncrementHit);
            DecrementHitCommand = new DelegateCommand(DecrementHit);
            ResetCommand = new DelegateCommand(ResetSplits);
            SetPbCommand = new DelegateCommand(SetPb);
            MarkTimesAsPbCommand = new DelegateCommand(MarkTimesAsPb);
            ExportRunDataCommand = new DelegateCommand(ExportRunData);
            SetDistancePbCommand = new DelegateCommand(SetDistancePb, CanSetDistancePb);

            ClearAllNotesCommand = new DelegateCommand(() =>
            {
                var confirmed = MsgBox.ShowOkCancel("This will clear all notes. Are you sure?", "Clear Notes");
                if (!confirmed) return;

                foreach (var split in Splits)
                    split.Notes = string.Empty;

                SaveNotes();
            });

            EditAttemptsCommand = new DelegateCommand(() => IsEditingAttempts = true);
            SaveNotesCommand = new DelegateCommand(SaveNotes);

            RenameSelectedSplitCommand = new DelegateCommand(() =>
            {
                if (SelectedSplit == null) return;
                SelectedSplit.IsEditing = true;
            });

            ResetSelectedSplitHitsCommand = new DelegateCommand(() =>
            {
                if (SelectedSplit == null || SelectedSplit.IsParent) return;
                SelectedSplit.NumOfHits = 0;
                _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
            });

            ResetSelectedSplitBossTimerCommand = new DelegateCommand(() =>
            {
                if (SelectedSplit == null || SelectedSplit.IsParent) return;

                // If a timer is actively running for this exact split, cancel it too --
                // otherwise the very next tick would immediately overwrite the reset.
                if (_bossTimerSplit == SelectedSplit)
                    ClearBossTimerState();

                SelectedSplit.BossKillTimeMs = null;
                _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
            });

            // Recovers whatever BossKillTimeMs was showing right before the last
            // non-kill clear (ResetBossTimer, HandleGameUnloaded, or the IGT-rewind
            // guard) wiped it -- see SnapshotBossTimerRestore. Lands paused (not
            // live-ticking): there's no sane igt reference left to resume a live
            // segment from after time has passed, so this mirrors ToggleBossTimer's
            // own paused-display state. Resume via the ToggleBossTimer hotkey if
            // wanted, same as any other paused timer.
            RestoreSelectedSplitBossTimerCommand = new DelegateCommand(() =>
            {
                if (SelectedSplit == null || SelectedSplit.IsParent) return;
                if (SelectedSplit.BossKillTimeRestoreMs is not { } restoreMs) return;

                var entry = GetActiveProfileSplitEntry(SelectedSplit);
                if (!IsBossTimerEligible(entry)) return;

                _bossTimerStartIgtMs = null;
                _bossTimerAccumulatedMs = restoreMs;
                _bossTimerIsPaused = true;
                _bossTimerSplit = SelectedSplit;
                _bossTimerFlag = entry.EventId;
                _bossTimerAwaitingRestoreResume = true;

                SelectedSplit.BossKillTimeMs = restoreMs;
                SaveRunState();
                _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
            });

            // Explicit override, distinct from StopBossTimer's automatic PB check --
            // always overwrites BossKillTimeBestMs regardless of whether the current
            // time is actually faster, per explicit user request.
            MarkCurrentTimeAsPbCommand = new DelegateCommand(() =>
            {
                if (SelectedSplit == null || SelectedSplit.IsParent) return;
                if (SelectedSplit.BossKillTimeMs is not { } ms) return;
                if (_activeProfile == null) return;

                var index = Splits.IndexOf(SelectedSplit);
                if (index < 0 || index >= _activeProfile.Splits.Count) return;

                SelectedSplit.BossKillTimeBestMs = ms;
                _activeProfile.Splits[index].BossKillTimeBestMs = ms;
                _profileService.SaveProfile(_activeProfile);
                _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
            });

            ClearTotalPbCommand = new DelegateCommand(() =>
            {
                var confirmed = MsgBox.ShowOkCancel("This will clear all personal bests. Are you sure?", "Clear PBs");
                if (!confirmed) return;

                foreach (var split in Splits.Where(s => s.Type == SplitType.Child))
                {
                    split.PersonalBest = 0;
                    var index = Splits.IndexOf(split);
                    if (index >= 0 && index < _activeProfile.Splits.Count)
                        _activeProfile.Splits[index].PersonalBest = 0;
                }

                if (_activeProfile != null)
                    _activeProfile.DistancePb = -1;

                _profileService.SaveProfile(_activeProfile);
                RefreshSplitValues();
                _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
            });

            EditSplitPbCommand = new DelegateCommand(() =>
            {
                if (SelectedSplit != null)
                    SelectedSplit.IsEditingPb = true;
            });

            EditSplitBossKillTimePbCommand = new DelegateCommand(() =>
            {
                if (SelectedSplit != null)
                    SelectedSplit.IsEditingBossKillTimePb = true;
            });

            ToggleLockCommand = new DelegateCommand(() =>
            {
                IsUnlocked = !IsUnlocked;
                SettingsManager.Default.IsUnlocked = IsUnlocked;
                SettingsManager.Default.Save();
                if (!IsUnlocked) SelectedSplit = null;
                MoveSplitUpCommand.RaiseCanExecuteChanged();
                MoveSplitDownCommand.RaiseCanExecuteChanged();
                SetDistancePbCommand.RaiseCanExecuteChanged();
            });

            MoveSplitUpCommand = new DelegateCommand(MoveSplitUp, () => CanMoveSplitUp());
            MoveSplitDownCommand = new DelegateCommand(MoveSplitDown, () => CanMoveSplitDown());
        }

        private void RegisterHotkeys()
        {
            _hotkeyManager.RegisterAction(HotkeyActions.NextSplit, ManualAdvanceSplit);
            _hotkeyManager.RegisterAction(HotkeyActions.PreviousSplit, () =>
            {
                ClearBossTimerState();
                _splitNav.Previous();
            });
            _hotkeyManager.RegisterAction(HotkeyActions.ToggleBossTimer, ToggleBossTimer);
            _hotkeyManager.RegisterAction(HotkeyActions.ResetBossTimer, ResetBossTimer);

            _hotkeyManager.RegisterAction(HotkeyActions.Reset, ResetSplits);
            _hotkeyManager.RegisterAction(HotkeyActions.IncrementHit, IncrementHit);
            _hotkeyManager.RegisterAction(HotkeyActions.DecrementHit, DecrementHit);
            _hotkeyManager.RegisterAction(HotkeyActions.StartTimer, () => _orchestrator.ManualStart());
            _hotkeyManager.RegisterAction(HotkeyActions.PauseTimer, () => _orchestrator.ManualStop());
            _hotkeyManager.RegisterAction(HotkeyActions.TogglePracticeMode,
                () =>
                {
                    if (_orchestrator.ActiveGame?.IsManual != true) IsPracticeMode = !IsPracticeMode;
                });
        }

        private void OnSplitStateChanged()
        {
            UpdateDistancePb();
            RefreshPastIndicator();
            OnPropertyChanged(nameof(CurrentSplit));
            OnPropertyChanged(nameof(CurrentSplitNumber));
            OnPropertyChanged(nameof(IsRunComplete));
            OnPropertyChanged(nameof(TotalHits));
            OnPropertyChanged(nameof(TotalDiff));
            OnPropertyChanged(nameof(TotalPb));
            SaveRunState();
            if (_orchestrator.ActiveGame == _selectedGame)
                _orchestrator.UpdateEvents(GetActiveEvents());
            _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
        }

        private void OnOrchestratorAttachmentChanged()
        {
            IsAttached = _orchestrator.IsAttached;
            AttachedText = _orchestrator.AttachedText;
            AttachmentStatus = _orchestrator.AttachmentStatus;
            OnPropertyChanged(nameof(TrackingText));

            // The auto-split event dictionary lives in the module's event service, which
            // is created lazily when the game attaches (module Initialize on
            // State.Attached), seeded from the events snapshot captured back at Track()
            // time. Any UpdateEvents call made before the service exists -- e.g. a Reset
            // done while the tool was still attaching/loading in -- silently no-ops
            // against the null service and is lost, so the first run's dictionary stays
            // stale (the boss being fought can be pre-marked as already-hit). Re-push the
            // current events once attached so those pre-attach changes actually take
            // effect. Idempotent and safe: it's the same call already made on every
            // split/profile change, GetActiveEvents recomputes Hit from the live cursor,
            // and it null-safely no-ops on the earlier AttachmentChanged fires that occur
            // before the service is created.
            if (_orchestrator.IsAttached && _orchestrator.ActiveGame == _selectedGame)
                _orchestrator.UpdateEvents(GetActiveEvents());
        }

        private void StartTrackingGame()
        {
            if (_selectedGame == null) return;
            _orchestrator.Track(_selectedGame);
            if (_selectedGame.IsManual && InGameTime.TotalMilliseconds > 0)
                _orchestrator.ManualSetElapsed((long)InGameTime.TotalMilliseconds);
            SettingsManager.Default.LastSelectedGame = _selectedGame.GameName;
            SettingsManager.Default.Save();
            OnPropertyChanged(nameof(TrackingText));
            OnPropertyChanged(nameof(TimerLabel));
        }

        private void AutoAdvanceSplit()
        {
            if (_selectedGame != _orchestrator.ActiveGame) return;
            if (IsPracticeMode) return;
            if (CurrentSplit == null) return;
            StopBossTimer();
            _splitNav.Advance();
        }

        private long? _bossTimerStartIgtMs;   // IGT when the current running segment began; null while paused/stopped
        private long _bossTimerAccumulatedMs; // elapsed folded in from segments before the current one
        private bool _bossTimerIsPaused;
        private SplitViewModel _bossTimerSplit;
        private uint? _bossTimerFlag;          // EventId of the fight currently being timed, for phase-transition detection

        // True only right after Restore Timer lands a split paused. A deliberate
        // ToggleBossTimer pause should stay frozen until the user explicitly resumes
        // it (a stray healthbar re-trigger shouldn't silently resume a break taken on
        // purpose) -- but a Restore-paused timer exists specifically to pick back up
        // where an unintentional reset left off, so the next real encounter of the
        // same flag should resume it automatically instead of sitting frozen forever.
        private bool _bossTimerAwaitingRestoreResume;

        private void HandleBossHealthBarSpawn(uint entityId)
        {
            if (!IsBossTimeTrackersEnabled(_selectedGame)) return;
            if (_selectedGame != _orchestrator.ActiveGame) return;
            if (IsPracticeMode || IsRunComplete || ActiveProfile == null) return;

            // Search forward from the current split rather than only matching CurrentSplit
            // itself -- mirrors how the existing flag-based auto-split (GetActiveEvents)
            // already tolerates the player being ahead of where the split cursor thinks
            // they are (e.g. attaching mid-run, or practicing a later boss directly).
            var cutoff = CurrentSplit != null ? Splits.IndexOf(CurrentSplit) : 0;
            if (cutoff < 0) cutoff = 0;

            var bossEntityIds = _gameModuleFactory.GetBossEntityIdsForGame(_selectedGame.Title);

            for (var i = cutoff; i < ActiveProfile.Splits.Count; i++)
            {
                var entry = ActiveProfile.Splits[i];
                if (entry is not { EventId: not null }) continue;
                if (!bossEntityIds.TryGetValue(entry.EventId.Value, out var expectedEntityIds)) continue;
                if (!expectedEntityIds.Contains(entityId)) continue;

                // Any healthbar spawn matching the flag already being timed -- a genuine
                // phase transition (new entity ID), or the bar simply hiding/reshowing for
                // the same entity (decapitation-style transitions, stealth-deathblow prompts,
                // etc.) -- is left alone. There's no reliable way to tell that apart from a
                // genuine wipe/retry using entity IDs alone (Guardian Ape's decapitation
                // reuses the same entity ID, so a "repeat means retry" rule was resetting a
                // clean, no-death run). Same policy as hit counting: no auto-detection of
                // wipes, the clock only resets via an explicit action -- the Reset Boss Timer
                // hotkey, or resetting the split/run.
                if (_bossTimerFlag == entry.EventId.Value)
                {
                    if (_bossTimerAwaitingRestoreResume)
                    {
                        _bossTimerStartIgtMs = (long)InGameTime.TotalMilliseconds;
                        _bossTimerIsPaused = false;
                        _bossTimerAwaitingRestoreResume = false;
                    }
                    return;
                }

                _bossTimerStartIgtMs = (long)InGameTime.TotalMilliseconds;
                _bossTimerAccumulatedMs = 0;
                _bossTimerIsPaused = false;
                _bossTimerSplit = i < Splits.Count ? Splits[i] : null;
                _bossTimerFlag = entry.EventId.Value;
                if (_bossTimerSplit != null) _bossTimerSplit.BossKillTimeMs = null;
                return;
            }
        }

        // DS2-only path: its boss-gauge struct has no per-boss identifier reachable
        // (see DS2BossGaugeService.cs comments -- the field that looked like one
        // stayed 0 through live testing), so there's no entity ID to search Splits
        // for. Instead this always attributes activation to CurrentSplit directly --
        // same semantics as the manual ToggleBossTimer hotkey below, just triggered
        // automatically instead of by a keypress. If CurrentSplit is already the one
        // being timed, this is a no-op (repeat activations from the same fight just
        // let the running clock continue, matching every other game's policy of
        // never auto-resetting on a repeat signal).
        private void HandleBossGaugeActivated()
        {
            if (!IsBossTimeTrackersEnabled(_selectedGame)) return;
            if (_selectedGame != _orchestrator.ActiveGame) return;
            if (IsPracticeMode || IsRunComplete || CurrentSplit == null) return;

            var entry = GetActiveProfileSplitEntry(CurrentSplit);
            if (!IsBossTimerEligible(entry)) return;

            if (_bossTimerSplit == CurrentSplit)
            {
                if (_bossTimerAwaitingRestoreResume)
                {
                    _bossTimerStartIgtMs = (long)InGameTime.TotalMilliseconds;
                    _bossTimerIsPaused = false;
                    _bossTimerAwaitingRestoreResume = false;
                }
                return;
            }

            _bossTimerStartIgtMs = (long)InGameTime.TotalMilliseconds;
            _bossTimerAccumulatedMs = 0;
            _bossTimerIsPaused = false;
            _bossTimerSplit = CurrentSplit;
            _bossTimerFlag = entry.EventId;
            CurrentSplit.BossKillTimeMs = null;
        }

        // Fires when the player leaves the game world (quitout to title, or any other
        // full unload) while a boss timer is actively tracking a split -- resets it
        // rather than leaving it running/paused against a now-stale IGT reference.
        // Deliberately narrower than the "no auto-reset on retry" policy elsewhere in
        // this file (see HandleBossHealthBarSpawn): that policy is about not confusing
        // a genuine phase transition/reshow for a wipe, but a full game-world unload is
        // an unambiguous signal the current attempt ended, not a case that policy was
        // meant to protect. Complements the IGT-rewind guard in UpdateInGameTime, which
        // still catches the case where the world reloads with IGT rewound before this
        // event's own IsLoaded()-transition would otherwise be observed.
        private void HandleGameUnloaded()
        {
            if (_selectedGame != _orchestrator.ActiveGame) return;
            if (_bossTimerSplit == null) return;

            var split = _bossTimerSplit;
            SnapshotBossTimerRestore(split);
            ClearBossTimerState();
            split.BossKillTimeMs = null;

            SaveRunState();
            _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
        }

        // Manual start/pause/resume toggle, bound to the ToggleBossTimer hotkey. Unlike
        // HandleBossHealthBarSpawn this always targets CurrentSplit directly (a manual
        // press has no detected entity to match against), and pausing folds the elapsed
        // segment into _bossTimerAccumulatedMs rather than finalizing/recording it --
        // the fight isn't over, so this deliberately doesn't touch BossKillTimeBestMs.
        private void ToggleBossTimer()
        {
            if (!IsBossTimeTrackersEnabled(_selectedGame)) return;
            if (_selectedGame != _orchestrator.ActiveGame) return;
            if (IsPracticeMode || IsRunComplete || CurrentSplit == null) return;

            if (_bossTimerSplit == CurrentSplit)
            {
                if (_bossTimerIsPaused)
                {
                    _bossTimerStartIgtMs = (long)InGameTime.TotalMilliseconds;
                    _bossTimerIsPaused = false;
                    _bossTimerAwaitingRestoreResume = false;
                }
                else
                {
                    if (_bossTimerStartIgtMs is { } startMs)
                        _bossTimerAccumulatedMs += (long)InGameTime.TotalMilliseconds - startMs;
                    _bossTimerStartIgtMs = null;
                    _bossTimerIsPaused = true;
                    CurrentSplit.BossKillTimeMs = _bossTimerAccumulatedMs;
                    SaveRunState();
                }
                return;
            }

            var entry = GetActiveProfileSplitEntry(CurrentSplit);
            if (!IsBossTimerEligible(entry)) return;

            _bossTimerStartIgtMs = (long)InGameTime.TotalMilliseconds;
            _bossTimerAccumulatedMs = 0;
            _bossTimerIsPaused = false;
            _bossTimerSplit = CurrentSplit;
            _bossTimerFlag = entry.EventId;
            CurrentSplit.BossKillTimeMs = null;
        }

        // Resets CurrentSplit's boss timer, bound to the ResetBossTimer hotkey. This fully
        // stops and clears tracking -- as if the split had never been encountered this
        // attempt -- rather than zeroing-but-continuing like a stopwatch reset. That
        // distinction matters because there's no "left the boss area" detection: without a
        // full clear, resetting mid-fight (e.g. after quitting out) would leave the clock
        // live-ticking from 0 forever, even while the player is standing outside the arena.
        // Only a genuine healthbar spawn (HandleBossHealthBarSpawn's normal reset/start
        // path) starts it running again from here.
        private void ResetBossTimer()
        {
            if (!IsBossTimeTrackersEnabled(_selectedGame)) return;
            if (_selectedGame != _orchestrator.ActiveGame) return;
            if (IsPracticeMode || IsRunComplete || CurrentSplit == null) return;

            var entry = GetActiveProfileSplitEntry(CurrentSplit);
            if (!IsBossTimerEligible(entry)) return;

            SnapshotBossTimerRestore(CurrentSplit);

            if (_bossTimerSplit == CurrentSplit)
                ClearBossTimerState();

            CurrentSplit.BossKillTimeMs = null;

            SaveRunState();
            _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
        }

        private void StopBossTimer()
        {
            var split = _bossTimerSplit;
            if (split == null) return;

            var elapsed = _bossTimerAccumulatedMs;
            if (!_bossTimerIsPaused && _bossTimerStartIgtMs is { } startMs)
                elapsed += (long)InGameTime.TotalMilliseconds - startMs;

            ClearBossTimerState();

            var entry = GetActiveProfileSplitEntry(split);
            if (!IsBossTimerEligible(entry)) return;
            if (elapsed < 0) return;

            split.BossKillTimeMs = elapsed;
            SaveRunState();
        }

        // Boss Time Trackers is a real per-game setting now (2026-08-14) -- each game has
        // its own SettingsManager flag/checkbox, so unchecking one game's box no longer
        // disables the feature for the other four. This only resolves which flag to read;
        // for the hook-based games (DS3/Sekiro/ER) the native hook is still installed
        // unconditionally at attach time regardless of this setting -- it just gates
        // whether MainViewModel acts on what the hook captures, same as before this
        // change. Making "unchecked" mean the hook is genuinely not installed/removed
        // live would need its own follow-up (see boss-timer.md resources doc).
        private bool IsBossTimeTrackersEnabled(Game game) => game?.Title switch
        {
            GameTitle.Sekiro => SettingsManager.Default.SKBossTimeTrackersEnabled,
            GameTitle.DarkSouls3 => SettingsManager.Default.DS3BossTimeTrackersEnabled,
            GameTitle.EldenRing => SettingsManager.Default.ERBossTimeTrackersEnabled,
            GameTitle.DarkSoulsRemastered => SettingsManager.Default.DSRBossTimeTrackersEnabled,
            GameTitle.DarkSouls2 => SettingsManager.Default.DS2BossTimeTrackersEnabled,
            _ => false
        };

        // A split is boss-timer-eligible once the global setting is on (already checked by
        // every caller before reaching here) and its EventId has a confirmed boss Entity ID
        // mapping -- i.e. no more per-split opt-in, the global toggle covers every boss
        // automatically, including ones added to the profile afterward.
        // DS2 is a special case: it has no per-boss entity-ID table at all (see
        // DS2BossGaugeService.cs/HandleBossGaugeActivated -- its gauge struct has no
        // reachable per-boss identifier, so activation always targets CurrentSplit
        // directly instead of searching for an entity match). Requiring an entity-ID
        // table entry here would make every DS2 split ineligible, silently blocking
        // the timer regardless of gauge activation -- any split with an EventId is
        // eligible for DS2, matching the game-agnostic gate the other four games use
        // via their own entity-ID tables.
        private bool IsBossTimerEligible(SplitEntry entry)
        {
            if (entry?.EventId is not { } eventId) return false;
            if (_selectedGame.Title == GameTitle.DarkSouls2) return true;
            return _gameModuleFactory.GetBossEntityIdsForGame(_selectedGame.Title).ContainsKey(eventId);
        }

        private void ClearBossTimerState()
        {
            _bossTimerStartIgtMs = null;
            _bossTimerAccumulatedMs = 0;
            _bossTimerIsPaused = false;
            _bossTimerSplit = null;
            _bossTimerFlag = null;
            _bossTimerAwaitingRestoreResume = false;
        }

        // Called right before a clear that would otherwise just discard whatever
        // BossKillTimeMs was showing (as opposed to StopBossTimer's proper finalize
        // path, which already logs it). Backs up the live/paused value so "Restore
        // Timer" can recover it later.
        private static void SnapshotBossTimerRestore(SplitViewModel split)
        {
            if (split?.BossKillTimeMs is { } ms) split.BossKillTimeRestoreMs = ms;
        }

        private SplitEntry GetActiveProfileSplitEntry(SplitViewModel split)
        {
            if (ActiveProfile == null || split == null) return null;
            var idx = Splits.IndexOf(split);
            return idx >= 0 && idx < ActiveProfile.Splits.Count ? ActiveProfile.Splits[idx] : null;
        }

        private void HandleRunStart()
        {
            if (_orchestrator.ActiveGame?.IsManual == true) return;
            if (_selectedGame != _orchestrator.ActiveGame) return;
            if (IsPracticeMode) return;
            if (!SettingsManager.Default.AutoResetOnNewGameStart) return;
            if (!HasRunProgress()) return;

            ResetRun();
        }

        private bool HasRunProgress() => CurrentSplitNumber > 1 || TotalHits > 0 || InGameTime > TimeSpan.Zero;

        private void ManualAdvanceSplit()
        {
            if (CurrentSplit == null) return;
            StopBossTimer();
            _splitNav.Advance();
        }

        private void UpdateInGameTime(long igt)
        {
            InGameTime = TimeSpan.FromMilliseconds(igt);
            var formatted = $"{(int)InGameTime.TotalHours}:{InGameTime.Minutes:D2}:{InGameTime.Seconds:D2}";
            if (formatted != _lastIgt)
            {
                _lastIgt = formatted;
                InGameTimeFormatted = formatted;
                _overlayServerService.BroadcastIgt(formatted);
            }

            // Live-updates the current split's boss timer every tick while one is running,
            // so it visibly ticks up during the fight and naturally freezes whenever IGT
            // itself stops advancing (loading screens, detached, etc.) rather than only
            // appearing once the kill flag fires. Throttled to once per displayed second
            // (same as the IGT text above) so the overlay isn't broadcast 10x/second.
            if (_bossTimerStartIgtMs is { } startMs && _bossTimerSplit != null)
            {
                var newElapsed = _bossTimerAccumulatedMs + (igt - startMs);

                // IGT reflects the save file's last checkpoint, not free-running real
                // time -- quitting out without an intervening autosave (e.g. mid-fight)
                // can make the readable IGT value drop on reload, discarding whatever
                // progress was made since the last checkpoint. That rewinds igt behind
                // this segment's own startMs, which would otherwise show as elapsed time
                // going negative (visually: jumping to a positive number, counting down
                // to 0, then counting back up as the new, lower igt trajectory catches
                // back up to the old startMs). Confirmed live 2026-08-16 -- not caused by
                // ResetRun/ClearBossTimerState (both ruled out via debug instrumentation).
                // Treat a rewound segment as invalid rather than displaying it: clear
                // tracking so the next genuine healthbar spawn starts a fresh segment
                // from the post-reload igt baseline instead of computing nonsense.
                if (newElapsed < 0)
                {
                    var split = _bossTimerSplit;
                    SnapshotBossTimerRestore(split);
                    ClearBossTimerState();
                    split.BossKillTimeMs = null;
                    return;
                }

                var previousDisplay = _bossTimerSplit.BossKillTimeDisplay;
                _bossTimerSplit.BossKillTimeMs = newElapsed;
                if (_bossTimerSplit.BossKillTimeDisplay != previousDisplay)
                    _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
            }
        }

        private ProfileEditorWindow _profileEditorWindow;
        private EventLogWindow _eventLogWindow;
        private EventLogViewModel _eventLogViewModel;

        private void OpenEventLog()
        {
            if (_eventLogWindow != null)
            {
                _eventLogWindow.Activate();
                return;
            }

            _eventLogViewModel = new EventLogViewModel();
            _eventLogWindow = new EventLogWindow { DataContext = _eventLogViewModel };

            _orchestrator.SetEventLogEnabled(true);

            _eventLogWindow.Closed += (s, e) =>
            {
                _orchestrator.SetEventLogEnabled(false);
                _eventLogWindow = null;
                _eventLogViewModel = null;
            };

            _eventLogWindow.Show();
        }

        private event Action ActiveProfileSplitsChanged;

        private void NotifyProfileSplitsChanged()
            => ActiveProfileSplitsChanged?.Invoke();

        private void OpenProfileEditor()
        {
            if (_selectedGame == null) return;

            if (_profileEditorWindow != null)
            {
                _profileEditorWindow.Activate();
                return;
            }

            var events = _selectedGame.IsManual
                ? new Dictionary<uint, string>()
                : GetAllEventsForGame(_selectedGame.Title);
            var vm = new ProfileEditorViewModel(
                events,
                _profileService,
                _selectedGame.GameName,
                _selectedGame.Title,
                _activeProfile,
                _selectedGame.IsManual);

            _profileEditorWindow = new ProfileEditorWindow { DataContext = vm };

            ActiveProfileSplitsChanged += vm.RefreshSplits;

            vm.OnSaved += () => ApplyProfileEditorSaved(vm.SelectedProfile?.Name);

            _profileEditorWindow.Closed += (s, e) =>
            {
                _profileEditorWindow = null;
                ApplyProfileEditorClosed();
            };

            _profileEditorWindow.Show();
        }

        internal void ApplyProfileEditorSaved(string selectedProfileName)
        {
            var updatedProfiles = _profileService.GetProfiles(_selectedGame.GameName);
            Profiles.Clear();
            foreach (var p in updatedProfiles)
                Profiles.Add(p);

            ActiveProfile = Profiles.FirstOrDefault(p => p.Name == selectedProfileName);
            _orchestrator.UpdateEvents(GetActiveEvents());
        }

        internal void ApplyProfileEditorClosed()
        {
            if (_activeProfile != null)
                _runStateService.Invalidate(_selectedGame.GameName, _activeProfile.Name);

            var validProfileNames = _profileService.GetProfiles(_selectedGame.GameName).Select(p => p.Name);
            _runStateService.InvalidateStale(_selectedGame.GameName, validProfileNames);
        }

        private void UpdateSplits()
        {
            foreach (var split in Splits)
                ((IDisposable)split).Dispose();

            Splits.Clear();
            if (ActiveProfile == null) return;
            if (ActiveProfile.Splits.Count == 0) return;

            foreach (var split in ActiveProfile.Splits)
            {
                var vm = new SplitViewModel
                {
                    Name = split.Label,
                    IsAuto = split.IsAuto,
                    Type = split.Type,
                    NumOfHits = 0,
                    PersonalBest = split.PersonalBest,
                    Notes = split.Notes,
                    BossKillTimeBestMs = split.BossKillTimeBestMs
                };
                vm.PropertyChanged += (_, _) =>
                {
                    OnPropertyChanged(nameof(TotalSplitCount));
                    OnPropertyChanged(nameof(AttemptCount));
                    OnPropertyChanged(nameof(TotalHits));
                    OnPropertyChanged(nameof(TotalDiff));
                    OnPropertyChanged(nameof(TotalHitsBrush));
                    OnPropertyChanged(nameof(TotalPb));
                    RefreshDistancePbIndicator();
                };
                Splits.Add(vm);
            }
        }

        private void MoveSplitUp()
        {
            if (!CanMoveSplitUp()) return;
            var split = SelectedSplit;
            var index = Splits.IndexOf(split);
            MoveItem(split, index - 1);
            SelectedSplit = split;
            MoveSplitUpCommand.RaiseCanExecuteChanged();
            MoveSplitDownCommand.RaiseCanExecuteChanged();
            SetDistancePbCommand?.RaiseCanExecuteChanged();
            NotifyProfileSplitsChanged();
        }

        private void MoveSplitDown()
        {
            if (!CanMoveSplitDown()) return;
            var split = SelectedSplit;
            var index = Splits.IndexOf(split);
            MoveItem(split, index + 2);
            SelectedSplit = split;
            MoveSplitUpCommand.RaiseCanExecuteChanged();
            MoveSplitDownCommand.RaiseCanExecuteChanged();
            NotifyProfileSplitsChanged();
        }

        private bool CanMoveSplitUp()
        {
            if (!IsUnlocked || SelectedSplit == null || SelectedSplit.IsParent) return false;
            var index = Splits.IndexOf(SelectedSplit);
            return index > 0 && !Splits[index - 1].IsParent;
        }

        private bool CanMoveSplitDown()
        {
            if (!IsUnlocked || SelectedSplit == null || SelectedSplit.IsParent) return false;
            var index = Splits.IndexOf(SelectedSplit);
            return index < Splits.Count - 1 && !Splits[index + 1].IsParent;
        }

        private static Brush GetBrush(string key)
        {
            if (Application.Current.Resources[key] is SolidColorBrush brush)
                return brush;
            return new SolidColorBrush(Colors.White);
        }

        private void UpdateDistancePb()
        {
            if (_activeProfile == null || CurrentSplit == null) return;
            if (TotalHits == 0)
                TryAdvanceDistancePb();
        }

        private void SetDistancePb()
        {
            if (_activeProfile == null || SelectedSplit == null || SelectedSplit.IsParent) return;

            var index = Splits.IndexOf(SelectedSplit);
            if (index < 0) return;

            _activeProfile.DistancePb = index;
            RefreshDistancePbIndicator();
            _profileService.SaveProfile(_activeProfile);
            _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
        }

        private bool CanSetDistancePb() =>
            _activeProfile != null && SelectedSplit != null && !SelectedSplit.IsParent;

        private void TryAdvanceDistancePb()
        {
            var currentIdx = Splits.IndexOf(CurrentSplit);
            if (currentIdx > _activeProfile.DistancePb)
            {
                _activeProfile.DistancePb = currentIdx;
                _profileService.SaveProfile(_activeProfile);
            }
        }

        private void RefreshDistancePbIndicator()
        {
            if (_activeProfile == null) return;
            for (int i = 0; i < Splits.Count; i++)
                Splits[i].IsDistancePb = i == _activeProfile.DistancePb;
        }

        private void RefreshPastIndicator()
        {
            var currentIndex = CurrentSplit != null ? Splits.IndexOf(CurrentSplit) : -1;
            for (int i = 0; i < Splits.Count; i++)
                Splits[i].IsPast = currentIndex >= 0 && (i < currentIndex || (IsRunComplete && i == currentIndex));
        }

        private void RefreshSplitValues()
        {
            var hits = Splits.Select(s => s.NumOfHits).ToArray();
            var bossKillTimes = Splits.Select(s => s.BossKillTimeMs).ToArray();
            var currentIndex = CurrentSplit != null ? Splits.IndexOf(CurrentSplit) : -1;
            var selectedIndex = SelectedSplit != null ? Splits.IndexOf(SelectedSplit) : -1;

            UpdateSplits();
            RefreshDistancePbIndicator();

            for (int i = 0; i < Splits.Count && i < hits.Length; i++)
            {
                Splits[i].NumOfHits = hits[i];
                Splits[i].BossKillTimeMs = bossKillTimes[i];
            }

            if (currentIndex >= 0 && currentIndex < Splits.Count)
            {
                CurrentSplit = Splits[currentIndex];
                CurrentSplit.IsCurrent = true;
            }

            if (selectedIndex >= 0 && selectedIndex < Splits.Count)
                SelectedSplit = Splits[selectedIndex];

            RefreshPastIndicator();
        }

        private Dictionary<uint, (string Name, int Required, int Hit)> GetActiveEvents()
        {
            if (ActiveProfile == null) return new();

            int cutoff;
            if (IsRunComplete)
                cutoff = ActiveProfile.Splits.Count;
            else if (CurrentSplit != null)
            {
                var idx = Splits.IndexOf(CurrentSplit);
                cutoff = idx >= 0 ? idx : 0;
            }
            else
                cutoff = 0;

            return ActiveProfile.Splits
                .Select((split, index) => (split, index))
                .Where(x => x.split.EventId.HasValue)
                .GroupBy(x => x.split.EventId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => (
                        Name: g.First().split.Label,
                        Required: g.Count(),
                        Hit: g.Count(x => x.index < cutoff)));
        }

        private Dictionary<uint, string> GetAllEventsForGame(GameTitle title) =>
            _gameModuleFactory.GetEventsForGame(title);

        private void LoadCustomGames()
        {
            foreach (var game in _customGameService.Load())
                Games.Add(game);
        }

        private void CreateCustomGame()
        {
            var input = MsgBox.ShowInput(
                "Create a game to add profiles and splits to.\nAuto hit counting and auto splitting are not supported,\nbut you can use a timer and track hits manually.",
                "", "New Custom Game");
            if (string.IsNullOrWhiteSpace(input)) return;

            var name = input.Trim();

            if (!CustomGameService.IsValidName(name))
            {
                MsgBox.Show("Name cannot contain ','.", "New Custom Game");
                return;
            }

            if (Games.Any(g => g.GameName == name))
            {
                MsgBox.Show("A game with that name already exists.", "New Custom Game");
                return;
            }

            var game = _customGameService.Add(name);
            Games.Add(game);

            SelectedGame = game;
            StartTrackingGame();
        }

        private void DeleteCustomGame()
        {
            if (_selectedGame == null || !_selectedGame.IsManual) return;

            var name = _selectedGame.GameName;
            var profiles = _profileService.GetProfiles(name);
            var count = profiles.Count;
            var profileMsg = count > 0
                ? $"\n\nThis will delete {count} profile{(count == 1 ? "" : "s")} and all splits associated with this game."
                : "";

            if (!MsgBox.ShowYesNo(
                    $"Are you sure you want to delete \"{name}\"?{profileMsg}",
                    "Delete Custom Game"))
                return;

            _customGameService.Delete(name);

            _orchestrator.Stop();
            OnPropertyChanged(nameof(TrackingText));
            OnPropertyChanged(nameof(TimerLabel));

            Games.Remove(_selectedGame);
            SelectedGame = Games.FirstOrDefault();
        }

        private void RenameCustomGame()
        {
            if (_selectedGame == null || !_selectedGame.IsManual) return;

            var oldName = _selectedGame.GameName;
            var input = MsgBox.ShowInput("Rename Game", oldName, "Rename Custom Game");
            if (string.IsNullOrWhiteSpace(input)) return;

            var newName = input.Trim();
            if (newName == oldName) return;

            if (!CustomGameService.IsValidName(newName))
            {
                MsgBox.Show("Name cannot contain ','.", "Rename Custom Game");
                return;
            }

            if (Games.Any(g => g.GameName == newName))
            {
                MsgBox.Show("A game with that name already exists.", "Rename Custom Game");
                return;
            }

            _customGameService.Rename(oldName, newName);

            var game = _selectedGame;
            game.GameName = newName;

            // Refresh the combo box display by re-inserting the item
            var index = Games.IndexOf(game);
            Games.RemoveAt(index);
            Games.Insert(index, game);
            _selectedGame = null;
            SelectedGame = game;

            SettingsManager.Default.LastSelectedGame = newName;
            SettingsManager.Default.Save();

            if (_orchestrator.ActiveGame == game)
                AttachedText = $"Custom Game: {newName}";
        }

        private void SaveNotes()
        {
            if (ActiveProfile == null) return;

            for (int i = 0; i < Splits.Count && i < ActiveProfile.Splits.Count; i++)
            {
                ActiveProfile.Splits[i].Notes = Splits[i].Notes;
            }

            _profileService.SaveProfile(ActiveProfile);
        }

        private void LoadProfile(Profile profile)
        {
            _runStateService.CancelPendingSave();
            IsRunComplete = false;
            UpdateSplits();
            if (profile != null && _runStateService.TryGet(_selectedGame?.GameName, profile.Name, out var snapshot))
                RestoreSnapshot(snapshot);
            else if (profile?.SavedRun != null)
                RestoreFromSavedRun(profile.SavedRun);
            else
                _splitNav.InitFresh();

            OnPropertyChanged(nameof(CurrentSplit));
            OnPropertyChanged(nameof(CurrentSplitNumber));
            OnPropertyChanged(nameof(IsRunComplete));
            OnPropertyChanged(nameof(TotalHits));
            OnPropertyChanged(nameof(TotalPb));
            OnPropertyChanged(nameof(TotalDiff));
            RefreshDistancePbIndicator();
            RefreshPastIndicator();
            _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
        }

        private void RestoreSnapshot(RunSnapshot snapshot)
        {
            var toRestore = _runStateService.RestoreSnapshot(Splits, snapshot);
            _splitNav.SetPosition(toRestore, snapshot.IsRunComplete);
            InGameTime = snapshot.InGameTime;
        }

        private void RestoreFromSavedRun(RunState state)
        {
            var toRestore = _runStateService.RestoreFromSavedRun(Splits, state);
            _splitNav.SetPosition(toRestore, state.IsRunComplete);
            InGameTime = TimeSpan.FromMilliseconds(state.IgtMilliseconds);
        }

        private void ResetSplits()
        {
            ResetRun();
        }

        private void ResetRun()
        {
            ClearBossTimerState();
            _runStateService.CancelPendingSave();
            UpdateDistancePb();

            if (_activeProfile != null)
            {
                _activeProfile.AttemptCount++;
                _activeProfile.SavedRun = null;
                _profileService.SaveProfile(_activeProfile);
                OnPropertyChanged(nameof(AttemptCount));
                RefreshDistancePbIndicator();
            }

            _runStateService.Invalidate(_selectedGame?.GameName, _activeProfile?.Name);
            UpdateSplits();
            _splitNav.InitFresh();

            _orchestrator.ManualReset();

            InGameTime = TimeSpan.Zero;
            InGameTimeFormatted = "0:00:00";

            if (_orchestrator.ActiveGame == _selectedGame)
                _orchestrator.UpdateEvents(GetActiveEvents());

            OnPropertyChanged(nameof(IsRunComplete));
            OnPropertyChanged(nameof(CurrentSplit));
            OnPropertyChanged(nameof(CurrentSplitNumber));
            RefreshPastIndicator();
            _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
            _overlayServerService.BroadcastIgt(InGameTimeFormatted);
        }

        private void SetPb()
        {
            for (int i = 0; i < Splits.Count && i < ActiveProfile.Splits.Count; i++)
            {
                if (Splits[i].IsParent) continue;
                Splits[i].PersonalBest = Splits[i].NumOfHits;
                ActiveProfile.Splits[i].PersonalBest = Splits[i].NumOfHits;
            }

            _profileService.SaveProfile(ActiveProfile);
            _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
        }

        // Unlike SetPb (hits), this only overwrites a split's time PB if the run's
        // actual time beats the existing one -- a split with no PB yet always gets
        // this run's time, but a slower time never overwrites a faster existing PB.
        private void MarkTimesAsPb()
        {
            if (ActiveProfile == null) return;

            for (int i = 0; i < Splits.Count && i < ActiveProfile.Splits.Count; i++)
            {
                if (Splits[i].IsParent) continue;
                if (Splits[i].BossKillTimeMs is not { } ms) continue;
                if (Splits[i].BossKillTimeBestMs is { } best && best <= ms) continue;

                Splits[i].BossKillTimeBestMs = ms;
                ActiveProfile.Splits[i].BossKillTimeBestMs = ms;
            }

            _profileService.SaveProfile(ActiveProfile);
            RefreshSplitValues();
            _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
        }

        private static string GetGameInitials(GameTitle? title) => title switch
        {
            GameTitle.DarkSoulsRemastered => "DSR",
            GameTitle.DarkSouls2 => "DS2",
            GameTitle.DarkSouls3 => "DS3",
            GameTitle.Sekiro => "SSDT",
            GameTitle.EldenRing => "ER",
            GameTitle.Manual => "Manual",
            _ => "Unknown"
        };

        private void ExportRunData()
        {
            if (ActiveProfile == null) return;

            var invalidChars = Path.GetInvalidFileNameChars();
            var safeName = new string(ActiveProfile.Name.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            var gameInitials = GetGameInitials(_selectedGame?.Title);

            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = $"{gameInitials}-{safeName}-{AttemptCount}",
                InitialDirectory = SettingsManager.Default.LastImportExportPath
            };

            if (dialog.ShowDialog() != true) return;

            SettingsManager.Default.LastImportExportPath = Path.GetDirectoryName(dialog.FileName);
            SettingsManager.Default.Save();

            var sb = new StringBuilder();
            sb.AppendLine("Split,Hits,PB,Time");

            foreach (var split in Splits.Where(s => s.Type == SplitType.Child))
            {
                sb.AppendLine(string.Join(",",
                    CsvEscape(split.Name),
                    split.NumOfHits.ToString(CultureInfo.InvariantCulture),
                    split.PersonalBest.ToString(CultureInfo.InvariantCulture),
                    CsvEscape(split.BossKillTimeDisplay)));
            }

            sb.AppendLine(string.Join(",",
                "TOTAL",
                TotalHits.ToString(CultureInfo.InvariantCulture),
                TotalPb.ToString(CultureInfo.InvariantCulture),
                CsvEscape(InGameTimeFormatted)));

            File.WriteAllText(dialog.FileName, sb.ToString());
        }

        private static string CsvEscape(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return field;
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        public void CommitPbEdit(SplitViewModel split, string value)
        {
            if (int.TryParse(value, out int val) && val >= 0)
            {
                split.PersonalBest = val;
                var index = Splits.IndexOf(split);
                if (index >= 0 && index < _activeProfile.Splits.Count)
                {
                    _activeProfile.Splits[index].PersonalBest = val;
                    _profileService.SaveProfile(_activeProfile);
                }
            }

            split.IsEditingPb = false;
            RefreshSplitValues();
            SetDistancePbCommand?.RaiseCanExecuteChanged();
            _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
        }

        public void CommitBossKillTimePbEdit(SplitViewModel split, string value)
        {
            if (TryParseBossKillTime(value, out var ms))
            {
                split.BossKillTimeBestMs = ms;
                var index = Splits.IndexOf(split);
                if (index >= 0 && index < _activeProfile.Splits.Count)
                {
                    _activeProfile.Splits[index].BossKillTimeBestMs = ms;
                    _profileService.SaveProfile(_activeProfile);
                }
            }

            split.IsEditingBossKillTimePb = false;
            RefreshSplitValues();
            _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
        }

        // Accepts the same "m:ss" shape BossKillTimeBestDisplay renders, plus a bare
        // seconds fallback for convenience. Invalid input is silently ignored (keeps
        // the existing PB), matching CommitPbEdit's own int.TryParse-guarded pattern.
        private static bool TryParseBossKillTime(string value, out long ms)
        {
            ms = 0;
            if (string.IsNullOrWhiteSpace(value)) return false;

            var parts = value.Split(':');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var minutes) && minutes >= 0
                && int.TryParse(parts[1], out var seconds) && seconds is >= 0 and < 60)
            {
                ms = (minutes * 60L + seconds) * 1000L;
                return true;
            }

            if (parts.Length == 1 && int.TryParse(parts[0], out var secondsOnly) && secondsOnly >= 0)
            {
                ms = secondsOnly * 1000L;
                return true;
            }

            return false;
        }

        private void IncrementHit()
        {
            if (IsRunComplete || CurrentSplit == null || IsPracticeMode) return;
            CurrentSplit.NumOfHits++;
            SaveRunState();
            _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
        }

        private void DecrementHit()
        {
            if (IsRunComplete || CurrentSplit == null || CurrentSplit.NumOfHits <= 0) return;
            CurrentSplit.NumOfHits--;
            SaveRunState();
            _overlayServerService.BroadcastState(OverlayMapper.MapFrom(this));
        }

        private void OnThemeChanged()
        {
            OnPropertyChanged(nameof(TotalHitsBrush));
        }

        #endregion
    }
}