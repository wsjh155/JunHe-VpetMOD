using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace VPet.Plugin.OnlineInteraction;

public class LogPanel : UserControl
{
	private TextBox? _logBox;

	private static readonly SolidColorBrush BorderBrushColor = new SolidColorBrush(Color.FromRgb(200, 200, 200));

	private static readonly SolidColorBrush HeaderBrush = new SolidColorBrush(Color.FromRgb(0, 150, 220));

	public LogPanel()
	{
		base.Background = Brushes.White;
		base.FontFamily = new FontFamily("Microsoft YaHei UI");
		BuildUI();
	}

	private void BuildUI()
	{
		Grid grid = new Grid
		{
			Margin = new Thickness(10.0)
		};
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(1.0, GridUnitType.Star)
		});
		DockPanel dockPanel = new DockPanel
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
		};
		TextBlock element = new TextBlock
		{
			Text = "互动日志",
			FontSize = 15.0,
			FontWeight = FontWeights.Bold,
			Foreground = HeaderBrush,
			VerticalAlignment = VerticalAlignment.Center
		};
		DockPanel.SetDock(element, Dock.Left);
		dockPanel.Children.Add(element);
		Button button = new Button
		{
			Content = "清空",
			Padding = new Thickness(8.0, 2.0, 8.0, 2.0),
			FontSize = 12.0,
			Cursor = Cursors.Hand
		};
		button.Click += delegate
		{
			_logBox?.Clear();
		};
		DockPanel.SetDock(button, Dock.Right);
		dockPanel.Children.Add(button);
		Grid.SetRow(dockPanel, 0);
		grid.Children.Add(dockPanel);
		_logBox = new TextBox
		{
			IsReadOnly = true,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
			FontSize = 13.0,
			BorderBrush = BorderBrushColor,
			BorderThickness = new Thickness(1.0),
			TextWrapping = TextWrapping.Wrap,
			AcceptsReturn = true,
			VerticalAlignment = VerticalAlignment.Stretch
		};
		Grid.SetRow(_logBox, 1);
		grid.Children.Add(_logBox);
		base.Content = grid;
	}

	public void AddLog(string msg)
	{
		((DispatcherObject)this).Dispatcher.Invoke((Action)delegate
		{
			if (_logBox != null)
			{
				_logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
				_logBox.ScrollToEnd();
			}
		});
	}
}
