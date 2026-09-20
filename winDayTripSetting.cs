using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace VPet.Plugin.DayTrip;

public class winDayTripSetting : Window, IComponentConnector
{
	internal Button CloseButton;

	private bool _contentLoaded;

	public winDayTripSetting(DayTripPlugin plugin)
	{
		InitializeComponent();
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "9.0.1.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/VPet.Plugin.DayTrip;component/windaytripsetting.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "9.0.1.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		if (connectionId == 1)
		{
			CloseButton = (Button)target;
		}
		else
		{
			_contentLoaded = true;
		}
	}
}
