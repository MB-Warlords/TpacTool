using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using GalaSoft.MvvmLight.Messaging;
using TpacTool.Lib;

namespace TpacTool
{
	public class AssetTreeViewModel : ViewModelBase
	{
		public static readonly Guid AssetSelectedEvent = Guid.NewGuid();

		public static readonly Guid RefreshEvent = Guid.NewGuid();

		private bool _isPackageMode;

		private string _filterText = string.Empty;

		private readonly AssetManager _manager;

		private readonly List<AssetViewModel.Item> _pendingDeletionNodes = new List<AssetViewModel.Item>();

		private AssetItem _selectedAsset;

		public List<AssetViewModel> PackageNodes { private set; get; }

		public List<AssetViewModel> AssetNodes { private set; get; }

		public ICommand ClearFilterCommand { private set; get; }

		public ICommand OpenInExplorerCommand { private set; get; }

		public ICommand CopyPathCommand { private set; get; }

		public ICommand SavePendingDeletionsCommand { private set; get; }

		public int PendingDeletionCount => _pendingDeletionNodes.Count;

		public string FilterText
		{
			set
			{
				if (_filterText != value)
				{
					_filterText = value;
					RaisePropertyChanged("FilterText");

					UpdateAssetTree();
				}
			}
			get
			{
				return _filterText;
			}
		}

		public bool IsPackageMode
		{
			set
			{
				_isPackageMode = value;
				UpdateAssetTree();
			}
			get => _isPackageMode;
		}

		private void UpdateAssetTree()
		{
			IEnumerable<AssetViewModel> list = null;

			if (_isPackageMode)
				list = PackageNodes;
			else
				list = AssetNodes;

			if (!string.IsNullOrWhiteSpace(_filterText))
				list = list.AsParallel().AsOrdered().Where(vm => vm.Filter(_filterText)).AsSequential();
			else
			{
				foreach (var assetViewModel in list)
				{
					assetViewModel.ClearFilter();
				}
			}

			TreeItemSource = list;
			RaisePropertyChanged("TreeItemSource");
		}

		public override void Cleanup()
		{
			PackageNodes.Clear();
			AssetNodes.Clear();
			FilterText = string.Empty;
			base.Cleanup();
		}

		public IEnumerable<AssetViewModel> TreeItemSource { private set; get; }

		private string _selectedFilePath;

		public string SelectedFilePath
		{
			get => _selectedFilePath;
			private set
			{
				_selectedFilePath = value;
				RaisePropertyChanged("SelectedFilePath");
			}
		}

		public AssetTreeViewModel(AssetManager manager, Guid typeGuid)
		{
			_manager = manager;
			ClearFilterCommand = new RelayCommand(() => { FilterText = string.Empty; });
			OpenInExplorerCommand = new RelayCommand(OpenInExplorer, () => !string.IsNullOrEmpty(SelectedFilePath));
			CopyPathCommand = new RelayCommand(CopyPath, () => !string.IsNullOrEmpty(SelectedFilePath));
			SavePendingDeletionsCommand = new RelayCommand(SavePendingDeletions, () => PendingDeletionCount > 0);

			MessengerInstance.Register<object>(this, RefreshEvent, _ => UpdateAssetTree());

			PackageNodes = new List<AssetViewModel>();
			AssetNodes = new List<AssetViewModel>();
			var tempList = new List<AssetViewModel>();

			foreach (var package in manager.LoadedPackages)
			{
				//var name = package.File.Name;
				bool hasAsset = false;
				var packageNode = new AssetViewModel.Package(this, package);
				foreach (var asset in package.Items)
				{
					if (asset.Type == typeGuid)
					{
						AssetViewModel assetNode = null;
						assetNode = new AssetViewModel.Item(this, asset);
						/*if (typeGuid == Texture.TYPE_GUID)	
							assetNode = new TextureTreeNode() { Asset = asset };
						else
							assetNode = new AssetTreeNode() { Asset = asset };*/
						AssetNodes.Add(assetNode);
						tempList.Add(assetNode);
						hasAsset = true;
					}
				}

				if (hasAsset)
				{
					tempList.Sort((left, right) =>
						{
							return StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
						});
					packageNode.AddRange(tempList);
					PackageNodes.Add(packageNode);
				}
				tempList.Clear();
			}

			PackageNodes.Sort((left, right) =>
				{
					return StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
				});
			AssetNodes.Sort((left, right) =>
				{
					return StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
				});

			IsPackageMode = true;
		}

		public void SelectAsset(AssetItem assetItem)
		{
			_selectedAsset = assetItem;
			SelectedFilePath = assetItem?.FilePath;
			MessengerInstance.Send(assetItem, AssetSelectedEvent);
		}

		public void MarkAssetForDeletion(AssetViewModel.Item node)
		{
			if (node == null || node.IsMarkedForDeletion)
				return;

			node.SetMarkedForDeletion(true);
			_pendingDeletionNodes.Add(node);
			UpdateAssetTree();
			RaisePendingDeletionChanged();
			MessengerInstance.Send($"Marked {node.Asset.Name} for deletion", MainViewModel.StatusEvent);
		}

		public void UnmarkAssetForDeletion(AssetViewModel.Item node)
		{
			if (node == null || !node.IsMarkedForDeletion)
				return;

			node.SetMarkedForDeletion(false);
			_pendingDeletionNodes.Remove(node);
			UpdateAssetTree();
			RaisePendingDeletionChanged();
			MessengerInstance.Send($"Unmarked {node.Asset.Name}", MainViewModel.StatusEvent);
		}

		private void SavePendingDeletions()
		{
			var pendingNodes = _pendingDeletionNodes.Where(node => node.IsMarkedForDeletion).Distinct().ToList();
			if (pendingNodes.Count == 0)
				return;

			var message = $"Delete {pendingNodes.Count} marked asset(s) and save affected TPAC package(s)?\n\n" +
						"This overwrites package files on disk.";
			if (MessageBox.Show(message, "Save Pending Deletions", MessageBoxButton.YesNo,
					MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
			{
				return;
			}

			var groups = new Dictionary<AssetPackage, List<PendingDeletion>>();
			foreach (var node in pendingNodes)
			{
				var asset = node.Asset;
				var package = _manager.GetPackage(asset);
				if (package == null || package.File == null)
				{
					MessageBox.Show($"Cannot find the package that owns \"{asset.Name}\".", "Save Pending Deletions",
						MessageBoxButton.OK, MessageBoxImage.Error);
					return;
				}

				var index = package.Items.IndexOf(asset);
				if (index < 0)
				{
					MessageBox.Show($"\"{asset.Name}\" is no longer present in its package.", "Save Pending Deletions",
						MessageBoxButton.OK, MessageBoxImage.Error);
					return;
				}

				if (!groups.TryGetValue(package, out var deletions))
				{
					deletions = new List<PendingDeletion>();
					groups[package] = deletions;
				}
				deletions.Add(new PendingDeletion(node, index));
			}

			var applied = new List<PendingDeletion>();
			foreach (var kvp in groups)
			{
				var package = kvp.Key;
				var deletions = kvp.Value.OrderByDescending(deletion => deletion.Index).ToList();

				foreach (var deletion in deletions)
					package.Items.RemoveAt(deletion.Index);

				try
				{
					package.Save();
					applied.AddRange(deletions);
				}
				catch (Exception ex)
				{
					foreach (var deletion in deletions.OrderBy(deletion => deletion.Index))
						package.Items.Insert(deletion.Index, deletion.Node.Asset);

					MessageBox.Show(ex.Message, "Save Pending Deletions Failed", MessageBoxButton.OK, MessageBoxImage.Error);
				}
			}

			if (applied.Count == 0)
				return;

			var removedSelectedAsset = applied.Any(deletion => ReferenceEquals(deletion.Node.Asset, _selectedAsset));
			foreach (var deletion in applied)
			{
				var asset = deletion.Node.Asset;
				_manager.RemoveAssetFromLookup(asset);
				_pendingDeletionNodes.Remove(deletion.Node);
				RemoveAssetNode(deletion.Node);
				NotifyDependentListsChanged(asset);
			}

			if (removedSelectedAsset)
				SelectAsset(null);

			RaisePendingDeletionChanged();
			MessengerInstance.Send($"Deleted {applied.Count} marked asset(s)", MainViewModel.StatusEvent);
			MessengerInstance.Send(_manager.WorkDir?.FullName, MainViewModel.ReloadAssetFolderEvent);
		}

		private void RemoveAssetNode(AssetViewModel.Item node)
		{
			AssetNodes.Remove(node);

			foreach (var packageNode in PackageNodes.OfType<AssetViewModel.Package>().ToArray())
			{
				if (packageNode.Remove(node) && !packageNode.HasItems)
					PackageNodes.Remove(packageNode);
			}

			UpdateAssetTree();
		}

		private void RaisePendingDeletionChanged()
		{
			RaisePropertyChanged(nameof(PendingDeletionCount));
			CommandManager.InvalidateRequerySuggested();
		}

		private void NotifyDependentListsChanged(AssetItem asset)
		{
			if (asset.Type == Skeleton.TYPE_GUID)
			{
				MessengerInstance.Send(_manager.LoadedAssets
					.Where(item => item.Type == Skeleton.TYPE_GUID && !item.Name.Contains("notused"))
					.Cast<Skeleton>().OrderBy(skeleton => skeleton.Name) as IEnumerable<Skeleton>,
					ModelViewModel.UpdateSkeletonListEvent);
			}
			else if (asset.Type == Metamesh.TYPE_GUID)
			{
				MessengerInstance.Send(_manager.LoadedAssets
					.Where(item => item.Type == Metamesh.TYPE_GUID)
					.Cast<Metamesh>().OrderBy(metamesh => metamesh.Name) as IEnumerable<Metamesh>,
					ModelViewModel.UpdateModelListEvent);
			}
		}

		private void OpenInExplorer()
		{
			if (!string.IsNullOrEmpty(SelectedFilePath))
			{
				try
				{
					if (File.Exists(SelectedFilePath))
					{
						Process.Start("explorer.exe", "/select,\"" + SelectedFilePath + "\"");
					}
					else
					{
						var directory = Path.GetDirectoryName(SelectedFilePath);
						if (Directory.Exists(directory))
						{
							Process.Start("explorer.exe", directory);
						}
					}
				}
				catch (Exception ex)
				{
					MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
				}
			}
		}

		private void CopyPath()
		{
			if (!string.IsNullOrEmpty(SelectedFilePath))
			{
				Clipboard.SetText(SelectedFilePath);
			}
		}

		private sealed class PendingDeletion
		{
			public AssetViewModel.Item Node { get; }

			public int Index { get; }

			public PendingDeletion(AssetViewModel.Item node, int index)
			{
				Node = node;
				Index = index;
			}
		}
	}
}
