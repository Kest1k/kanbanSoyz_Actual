/// <summary>
/// Кнопку Канбана можно показывать всегда: это просто вход на экран доски.
/// </summary>
/// <returns>true, если команду оставляем доступной.</returns>
public override bool IsValid()
{
    return true;
}

/// <summary>
/// Держим кнопку активной в меню/панели.
/// </summary>
/// <param name="cmdUI">Контрол команды в интерфейсе PLM.</param>
public override void UpdateState( ICmdUI cmdUI )
{
    cmdUI.Enabled = true;
}

/// <summary>
/// Открывает доску или активирует уже открытую вкладку.
/// </summary>
/// <param name="session">Сессия пользователя. Может быть null при серверном вызове.</param>
/// <param name="inputParams">Параметры здесь не используются.</param>
/// <returns>Всегда null: команда только открывает UI.</returns>
public override Object Invoke( UserSession session, Object inputParams )
{
    var root = Service.GetDataContainer( @"APSsServiceDataRootDirectory\UI" );
    var page =
        Service.GetInfoObjectOrContainer( @"APSsServiceDataRootDirectory\UI\Screens\myKanbanTest" ) ??
        Service.GetInfoObjectOrContainer( root, @"Screens\myKanbanTest" ) ??
        Service.GetInfoObjectOrContainer( root, @"Custom\Screens\myKanbanTest" );

    if( page != null )
    {
        var panel = (IBrowserPanel)Service.UI.GetBrowserPanelsFromAllGroups()
            .OfType<IScriptingObject>()
            .FirstOrDefault( o => o.ScriptingObject == page );

        if( panel != null )
            panel.Activate();
        else
            Service.UI.OpenPropertiesPane( page );
    }

    return null;
}
