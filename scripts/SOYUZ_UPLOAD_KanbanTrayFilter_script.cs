// Функция: убрать Kanban-нагрузки ExclamationKanban из штатных Windows/PLM toast-уведомлений.
// Тип скрипта в PLM: ClientAutomation / OnPostTrayNotification.
// Наше окно всё равно показывается в ExclamationKanban.OnUpdated.

private const string KANBAN_EXCLAMATION_TEMPLATE_KEY = "ExclamationKanban";

public override bool OnPostTrayNotification( System.Collections.Generic.List<AttributableObject> objects )
{
    if( objects == null || objects.Count == 0 ) return false;

    for( int i = objects.Count - 1; i >= 0; i-- )
    {
        var workItem = objects[i] as WorkItem;
        if( !IsKanbanExclamation( workItem ) ) continue;

        try { workItem.MarkAsViewedByCurrentUser(); } catch { }
        objects.RemoveAt( i );
    }

    // Это фильтр списка, а не полная обработка трея: остальные уведомления PLM идут штатно.
    return false;
}

private bool IsKanbanExclamation( WorkItem workItem )
{
    if( workItem == null ) return false;

    try
    {
        var template = workItem.Template;
        if( template != null )
        {
            var nameKey = template.NameKey ?? "";
            if( nameKey == KANBAN_EXCLAMATION_TEMPLATE_KEY ) return true;

            var nameUi = template.NameUI ?? "";
            if( nameUi == KANBAN_EXCLAMATION_TEMPLATE_KEY ) return true;
        }
    }
    catch { }

    return false;
}
