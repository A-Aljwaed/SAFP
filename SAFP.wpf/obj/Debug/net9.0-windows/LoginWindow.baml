   M S B A M L   `   `   `  ����C  ?SAFP.wpf, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null#clr-namespace:SAFP.WpfSAFP.Wpf  5   	       SAFP.Wpf.LoginWindow   5   	   W SPresentationCore, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\ XPresentationFramework, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35e aSystem.Windows.Controls.Ribbon, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089R NWindowsBase, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35^ ZWindowsFormsIntegration, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35H 9http://schemas.microsoft.com/winfx/2006/xaml/presentation      5   	   R NSystem.Xaml, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089:x,http://schemas.microsoft.com/winfx/2006/xaml     5   	   @d2http://schemas.microsoft.com/expression/blend/2008     5   	   Jmc;http://schemas.openxmlformats.org/markup-compatibility/2006     5   	   (localclr-namespace:SAFP.Wpf     5   	   $	��280��5   '   $	��400��64     � WindowStartupLocation$  CenterScreen=�6@    � 
ResizeMode$ NoResize=�5	   	   !  System.Windows.SystemColors   ControlBrushKey#���  5
   	    � UseLayoutRounding$
 True��5   	   &   System.Windows.Media.TextOptions   TextFormattingMode$ Display=�5   	    � Title 5   	   �� *WindowTitle+.��5      � $��20q�6    � RowDefinitions 5   
   �� 5      $
��Auto �6   �� 6-   $
��Auto �6;   �� 6L   $
��Auto �6Z   �� 6k   $
��Auto �6y   �� 6�   $��* �6�   .+�5   
   �� $��0��6   $��16(�5      $��SemiBold"�6"   $��Center=�5      $��0,0,0,15q�61   ��5   !   �� *
PromptText+5   1   G� 5   
   -   5         G�Name$ PasswordBox��5      $��1��6+   $	��250��68   $��Center=�5      $��Center=�63   $��14(�5      $��5q�6$   $��0,0,0,15q�60    � ToolTip$  Enter your master password��5      5      �� 5      $��2��6"   	 �� Orientation$	 
Horizontal=�6/   $��Center=�6H   $��0,10,0,5q�6e   .+�5      �� $	��120��5      $
��10,5q�6$   $��14(�63   $��SemiBold"�6A   $��5q�5       
 �� 	IsDefault$

 True��6    ��5      �� *ActionButtonText+��5      �� *SubmitCommand+5        �� 5!      $��Cancel Setup��6   $	��100��5$      $
��10,5q�6%   $��14(�64   $��5q�5%       �� IsCancel$
 True��6!   ��5"      �� *CancelSetupCommand+y�5#      �� *CancelButtonVisibility+5%   !   5&      �� 5)   
   $��3��6   
���  ��$��Center=�5*      $
��Wrap=�61   $��0,5,0,0q�6E   $��30��6V   ��5)   !   �� *StatusMessage+5*   V   5+      5,      