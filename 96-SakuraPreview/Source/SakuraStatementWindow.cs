// V3.2 ships only the response list. This native XUi window restores companion dialogue text.
public sealed class XUiC_SakuraStatement : XUiC_DialogStatementWindow
{
    Dialog bound;
    DialogStatement statement;
    public override void Update(float dt)
    {
        var model=xui.Dialog;
        bool visible=model.Respondent is SakuraPreview.EntitySakura;
        ViewComponent.IsVisible=visible;
        if(visible)
        {
            var dialog=model.DialogWindowGroup?.CurrentDialog;
            if(dialog!=bound || dialog?.CurrentStatement!=statement)
            {
                bound=dialog;statement=dialog?.CurrentStatement;
                CurrentDialog=dialog;Refresh();
            }
        }
        base.Update(dt);
    }
    public override void OnClose(){bound=null;statement=null;base.OnClose();}
}
