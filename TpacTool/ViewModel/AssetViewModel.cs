using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.CommandWpf;
using TpacTool.Lib;

namespace TpacTool
{
	public abstract class AssetViewModel : ViewModelBase
	{
		private bool _isSelected;

		protected AssetTreeViewModel _parentVM;

		public abstract string Name { get; }

		public ICommand OpenInExplorerCommand => _parentVM.OpenInExplorerCommand;

		public ICommand CopyPathCommand => _parentVM.CopyPathCommand;

		public virtual ICommand MarkForDeletionCommand => null;

		public virtual ICommand UnmarkDeletionCommand => null;

		public ICommand SavePendingDeletionsCommand => _parentVM.SavePendingDeletionsCommand;

		public virtual bool CanMarkForDeletion => false;

		public virtual bool CanUnmarkDeletion => false;

		public virtual bool IsMarkedForDeletion => false;

		public abstract bool HasItems { get; }

		public abstract IEnumerable<AssetViewModel> Children { get; }

		public virtual bool IsSelected
		{
			get => _isSelected;
			set => _isSelected = value;
		}

		public bool IsExpanded { get; set; }

		public abstract bool Filter(string filterText);

		public virtual void ClearFilter()
		{
		}

		protected AssetViewModel(AssetTreeViewModel parentVm)
		{
			_parentVM = parentVm;
		}

		public override string ToString()
		{
			return Name;
		}

		public sealed class Package : AssetViewModel
		{
			private readonly AssetPackage _package;

			private List<AssetViewModel> _children = new List<AssetViewModel>();

			private IEnumerable<AssetViewModel> _filteredChildren = null;

			public override string Name => _package.File.Name; // TODO: if null?

			public override bool HasItems => _children.Count > 0;

			public override IEnumerable<AssetViewModel> Children => _filteredChildren;

			public Package(AssetTreeViewModel parentVm, AssetPackage package) : base(parentVm)
			{
				_package = package;
				_filteredChildren = _children;
			}

			public void Add(AssetViewModel child)
			{
				_children.Add(child);
			}

			public void AddRange(IEnumerable<AssetViewModel> children)
			{
				_children.AddRange(children);
			}

			public bool Remove(AssetViewModel child)
			{
				var removed = _children.Remove(child);
				if (_filteredChildren == null || ReferenceEquals(_filteredChildren, _children))
					_filteredChildren = _children;
				return removed;
			}

			public override bool Filter(string filterText)
			{
				_filteredChildren = _children.Where(vm => vm.Filter(filterText)).AsEnumerable();
				return _filteredChildren.Any();
			}

			public override void ClearFilter()
			{
				_filteredChildren = _children;
			}

			/*private bool Equals(Package other)
			{
				return Equals(_package, other._package);
			}

			public override bool Equals(object obj)
			{
				if (ReferenceEquals(null, obj)) return false;
				if (ReferenceEquals(this, obj)) return true;
				return obj is Package other && Equals(other);
			}

			public override int GetHashCode()
			{
				return (_package != null ? _package.GetHashCode() : 0);
			}*/
		}

		public sealed class Item : AssetViewModel
		{
			private static IList<AssetViewModel> _empty_collection = new List<AssetViewModel>().AsReadOnly();

			private readonly AssetItem _asset;

			private readonly ICommand _markForDeletionCommand;

			private readonly ICommand _unmarkDeletionCommand;

			private bool _isMarkedForDeletion;

			public override string Name => IsMarkedForDeletion ? "[DELETE] " + _asset.Name : _asset.Name;

			public AssetItem Asset => _asset;

			public override bool IsMarkedForDeletion => _isMarkedForDeletion;

			public override ICommand MarkForDeletionCommand => _markForDeletionCommand;

			public override ICommand UnmarkDeletionCommand => _unmarkDeletionCommand;

			public override bool CanMarkForDeletion => !IsMarkedForDeletion;

			public override bool CanUnmarkDeletion => IsMarkedForDeletion;

			public override bool IsSelected
			{
				get => base.IsSelected;
				set
				{
					base.IsSelected = value;
					if (value && _asset != null)
						_parentVM.SelectAsset(_asset);
				}
			}

			public override bool HasItems => false;

			public override IEnumerable<AssetViewModel> Children => _empty_collection;

			public Item(AssetTreeViewModel parentVm, AssetItem asset) : base(parentVm)
			{
				_asset = asset;
				_markForDeletionCommand = new RelayCommand(() => _parentVM.MarkAssetForDeletion(this), () => CanMarkForDeletion);
				_unmarkDeletionCommand = new RelayCommand(() => _parentVM.UnmarkAssetForDeletion(this), () => CanUnmarkDeletion);
			}

			public void SetMarkedForDeletion(bool value)
			{
				if (_isMarkedForDeletion == value)
					return;

				_isMarkedForDeletion = value;
				RaisePropertyChanged(nameof(Name));
				RaisePropertyChanged(nameof(IsMarkedForDeletion));
				RaisePropertyChanged(nameof(CanMarkForDeletion));
				RaisePropertyChanged(nameof(CanUnmarkDeletion));
			}

			public override bool Filter(string filterText)
			{
				return Name.Contains(filterText);
			}

			/*private bool Equals(Item other)
			{
				return Equals(_asset, other._asset);
			}

			public override bool Equals(object obj)
			{
				if (ReferenceEquals(null, obj)) return false;
				if (ReferenceEquals(this, obj)) return true;
				return obj is Item other && Equals(other);
			}

			public override int GetHashCode()
			{
				return (_asset != null ? _asset.GetHashCode() : 0);
			}*/
		}
	}
}
