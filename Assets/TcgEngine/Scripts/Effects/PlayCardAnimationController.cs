using UnityEngine;

public class PlayCardAnimationController : MonoBehaviour
{
    [SerializeField]
    private RectTransform cardUI;
    
    [SerializeField]
    private Animator animator;
    
    [SerializeField]
    private float initialYPosition = 0f;
    
    [SerializeField]
    private float initialYSlope = 0f;
    
    private void Awake()
    {
        if (cardUI == null)
        {
            cardUI = GetComponent<RectTransform>();
        }
        
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
    }
    
    public void PlayCardAnimation(float startY, float slope = 0f)
    {
        // 设置卡牌的初始 Y 轴位置
        Vector2 anchoredPosition = cardUI.anchoredPosition;
        anchoredPosition.y = startY;
        cardUI.anchoredPosition = anchoredPosition;
        
        // 播放基础动画
        animator.Play("PlayCardBaseFX", 0, 0f);
    }
    
    public void PlayCardAnimation()
    {
        PlayCardAnimation(initialYPosition, initialYSlope);
    }
    
    public void PlayPlayerCardAnimation()
    {
        // 玩家出牌动画：从 0 位置开始
        PlayCardAnimation(0f, 0f);
    }
    
    public void PlayOtherCardAnimation()
    {
        // 其他玩家出牌动画：从 792 位置开始，带有斜率
        PlayCardAnimation(792f, -21.098913f);
    }
}