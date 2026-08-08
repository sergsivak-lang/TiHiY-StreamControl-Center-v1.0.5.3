using TiHiY.StreamControlCenter.Services;

namespace TiHiY.StreamControlCenter;

public sealed class DiscordProfilesEditorWindow : Window
{
    private readonly LiteCoreService _core;
    private readonly ListBox _list = new();
    private readonly TextBox _serverId = Box(), _serverName = Box(), _streamChannels = Box(64,true), _streamMention = Box(), _streamTemplate = Box(90,true), _moneyChannels = Box(64,true), _moneyMention = Box(), _moneyTemplate = Box(90,true);
    private readonly CheckBox _enabled = new() { Content = "Профіль увімкнено", IsChecked = true };
    private readonly TextBlock _status = new() { Foreground = Brushes.LimeGreen };

    public DiscordProfilesEditorWindow(LiteCoreService core)
    {
        _core = core;
        Title = "Discord профілі серверів"; Width = 980; Height = 720; MinWidth = 820; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.FindResource("Bg"); Foreground = (Brush)Application.Current.FindResource("Text");
        var root = new Grid { Margin = new Thickness(12) }; Content = root;
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        var left = new DockPanel { Margin = new Thickness(0,0,10,0) }; Grid.SetColumn(left,0); root.Children.Add(left);
        var title = new TextBlock { Text="ПРОФІЛІ DISCORD", FontSize=17, FontWeight=FontWeights.Black, Foreground=(Brush)Application.Current.FindResource("Amber"), Margin=new Thickness(4,4,4,10) }; DockPanel.SetDock(title,Dock.Top); left.Children.Add(title);
        _list.DisplayMemberPath = "ServerName"; _list.SelectionChanged += (_,_) => LoadSelected(); left.Children.Add(_list);
        var edit = new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto }; Grid.SetColumn(edit,1); root.Children.Add(edit);
        var form = new StackPanel(); edit.Content=form;
        Add(form,"Server ID",_serverId); Add(form,"Назва сервера",_serverName); Add(form,"Канали СТРІМІВ (ID через кому/новий рядок)",_streamChannels); Add(form,"Тег стрімів (@everyone / @here / <@&ROLE_ID>)",_streamMention); Add(form,"Текст стрімів: {platform} {title} {url} {viewers} {likes} {server}",_streamTemplate); Add(form,"Канали ДОНАТІВ / ПІДПИСОК",_moneyChannels); Add(form,"Тег монетизації",_moneyMention); Add(form,"Текст: {user} {kind} {amount} {currency} {source} {message} {url} {server}",_moneyTemplate); form.Children.Add(_enabled);
        var bottom = new DockPanel { Margin=new Thickness(0,8,0,0) }; Grid.SetRow(bottom,1); Grid.SetColumnSpan(bottom,2); root.Children.Add(bottom); bottom.Children.Add(_status);
        var buttons = new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right }; DockPanel.SetDock(buttons,Dock.Right); bottom.Children.Add(buttons);
        buttons.Children.Add(Make("НОВИЙ",(_,_)=>NewProfile())); buttons.Children.Add(Make("ЗБЕРЕГТИ",(_,_)=>SaveSelected())); buttons.Children.Add(Make("ТЕСТ СТРІМ",async(_,_)=>await TestStream())); buttons.Children.Add(Make("ТЕСТ ДОНАТ",async(_,_)=>await TestMoney())); buttons.Children.Add(Make("ЗАКРИТИ",(_,_)=>Close()));
        Reload();
    }

    private static TextBox Box(double height=34,bool multi=false) => new() { Height=height, AcceptsReturn=multi, TextWrapping=multi?TextWrapping.Wrap:TextWrapping.NoWrap, Margin=new Thickness(0,3,0,8), VerticalScrollBarVisibility=multi?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden };
    private static Button Make(string text,RoutedEventHandler click) { var b=new Button { Content=text, Margin=new Thickness(3) }; b.Click += click; return b; }
    private static void Add(Panel p,string label,Control c) { p.Children.Add(new TextBlock { Text=label, Foreground=(Brush)Application.Current.FindResource("Muted"), Margin=new Thickness(0,4,0,0) }); p.Children.Add(c); }
    private void Reload() { var selected=(_list.SelectedItem as DiscordServerProfile)?.ServerId; _list.ItemsSource=null; _list.ItemsSource=_core.Discord.Profiles.Profiles; if(!string.IsNullOrWhiteSpace(selected)) _list.SelectedItem=_core.Discord.Profiles.Profiles.FirstOrDefault(x=>x.ServerId==selected); }
    private void LoadSelected() { if(_list.SelectedItem is not DiscordServerProfile p)return; _serverId.Text=p.ServerId;_serverName.Text=p.ServerName;_streamChannels.Text=string.Join(Environment.NewLine,p.StreamChannelIds);_streamMention.Text=p.StreamMention;_streamTemplate.Text=p.StreamTemplate;_moneyChannels.Text=string.Join(Environment.NewLine,p.MonetizationChannelIds);_moneyMention.Text=p.MonetizationMention;_moneyTemplate.Text=p.MonetizationTemplate;_enabled.IsChecked=p.Enabled; }
    private void NewProfile(){_list.SelectedItem=null;_serverId.Clear();_serverName.Clear();_streamChannels.Clear();_streamMention.Text="@everyone";_streamTemplate.Text="🔴 {platform}: трансляція почалася!\n{title}\n{url}";_moneyChannels.Clear();_moneyMention.Clear();_moneyTemplate.Text="⭐ {user}: {kind}\n{amount} {currency}\n{message}";_enabled.IsChecked=true;}
    private void SaveSelected(){try{if(string.IsNullOrWhiteSpace(_serverId.Text))throw new InvalidOperationException("Вкажіть Server ID.");var p=new DiscordServerProfile{ServerId=_serverId.Text.Trim(),ServerName=string.IsNullOrWhiteSpace(_serverName.Text)?_serverId.Text.Trim():_serverName.Text.Trim(),StreamChannelIds=Ids(_streamChannels.Text),MonetizationChannelIds=Ids(_moneyChannels.Text),StreamMention=_streamMention.Text.Trim(),StreamTemplate=_streamTemplate.Text,MonetizationMention=_moneyMention.Text.Trim(),MonetizationTemplate=_moneyTemplate.Text,Enabled=_enabled.IsChecked==true};_core.Discord.Profiles.Update(p);_status.Text="Збережено";Reload();}catch(Exception ex){_status.Text=ex.Message;}}
    private async Task TestStream(){try{SaveSelected();await _core.Discord.TestProfileStreamsAsync(_serverId.Text.Trim());_status.Text="Тест стріму надіслано";}catch(Exception ex){_status.Text=ex.GetBaseException().Message;}}
    private async Task TestMoney(){try{SaveSelected();await _core.Discord.TestProfileMonetizationAsync(_serverId.Text.Trim());_status.Text="Тест монетизації надіслано";}catch(Exception ex){_status.Text=ex.GetBaseException().Message;}}
    private static List<string> Ids(string s)=>s.Split(new[]{',',';','\r','\n',' '},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();
}
