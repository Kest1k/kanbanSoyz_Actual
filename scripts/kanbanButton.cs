/// <summary>
/// Проверка применимости данного автоматического действия в текущем окружении
/// </summary>
/// <returns>Возвращает true, если автоматическое действие может работать в текущем окружении</returns>
public override bool IsValid()
{
    return true;
}

/// <summary>
/// Обновление состояния элементов интерфейса
/// </summary>
/// <param name="cmdUI">Объект для управления контролом вызова команды</param>
public override void UpdateState( ICmdUI cmdUI )
{
    cmdUI.Enabled = true;
}

/// <summary>
/// Вызов метода, реализованного скриптовой функцией
/// </summary>
/// <param name="session">Пользовательская сессия, инициировавшая вызов
/// (может быть null, если вызов инициировал серверный скрипт без указания сессии)</param>
/// <param name="inputParams">Сериализуемые входные данные для метода</param>
/// <returns>Результат работы метода</returns>
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
