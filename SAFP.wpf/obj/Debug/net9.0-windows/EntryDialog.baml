   M S B A M L   `   `   `  ����C  ?SAFP.wpf, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null#clr-namespace:SAFP.WpfSAFP.Wpf  5   	       SAFP.Wpf.EntryDialog   5   	   W SPresentationCore, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\ XPresentationFramework, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35e aSystem.Windows.Controls.Ribbon, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089R NWindowsBase, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35^ ZWindowsFormsIntegration, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35H 9http://schemas.microsoft.com/winfx/2006/xaml/presentation      5   	   R NSystem.Xaml, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089:x,http://schemas.microsoft.com/winfx/2006/xaml     5   	   @d2http://schemas.microsoft.com/expression/blend/2008     5   	   Jmc;http://schemas.openxmlformats.org/markup-compatibility/2006     5   	   (localclr-namespace:SAFP.Wpf     5   	   $	��520��5   '   $	��480��64     � WindowStartupLocation$  CenterOwner=�5	   	    � 
ResizeMode$ NoResize=�6-    � ShowInTaskbar$ False��6C   !  System.Windows.SystemColors   ControlBrushKey#���  5
   	    � UseLayoutRounding$
 True��5   	   &   System.Windows.Media.TextOptions   TextFormattingMode$ Display=�6"    � Title 5   	   �� *WindowTitle+.��5      � $��15q�6    � RowDefinitions 5   
   �� 5      $
��Auto �6   �� 6-   $
��Auto �6;   �� 6L   $
��Auto �6Z   �� 6k   $
��Auto �6y   �� 6�   $
��Auto �6�   �� 6�   $��* �6�   �� 6�   $
��Auto �6�   �� 6�   $
��Auto �6�    � ColumnDefinitions 5   
   �� 5      $
��Auto �6   �� 6/   $��* �6@   .+�5   
   �� $��0��6   $��0��6   $��
_Category:��6-   $��Center=�6r   	 �� Target	 6B   �� 
 �� ElementName$
 CategoryComboBox��6r   �� 5   
   -   5         ��Name$ CategoryComboBox��5   0   $��0��6   $��1��6    $��5q�6J   $��3q�6U   ��5      �� *
Categories+ �� SelectedItem 66   �� *Entry.Category+�� 5   
   $��1��6   $��0��6   $��_Service/Website:��6-   $��Center=�6w   	 6I   �� $
 ServiceTextBox��6w   �� 5   
   -   5         ��Name$ ServiceTextBox��5   /   $��1��6   $��1��6   $��5q�6G   $��3q�6R   ��5      �� *Entry.Service+ �� UpdateSourceTrigger$ PropertyChanged=��� 5   
   $��2��6   $��0��6   $��_Username/Login:��6-   $��Center=�6w   	 6H   �� $
 UsernameTextBox��6w   �� 5   
   -   5        $ UsernameTextBox��5   /   $��2��6   $��1��6   $��5q�6H   $��3q�6S   ��5      �� *Entry.Username+$ PropertyChanged=��� 5   
   $��3��6   $��0��6   $��
_Password:��6-   $��Center=�6m   	 6B   �� $
 PasswordBox��6m   � 5   
   $��3��6   $��1��6   $��5,5,5,0q�6,    5       �� 5!      $��* �6#   �� 5"      $
��Auto �6#   .+�5$      G� -   5         G�Name$ PasswordEntryBox��5$   *   $��0��6   $��3q�6D   $��Center=�6P   5%      �� 5&      -   5         ��Name$ ShowHideButton��5&   2   $��1��6"   $
��Show��6J   $��5,0,0,0q�6Y   $	��5,2q�6j   5'      5(      �� 5*   
   $��4��6   $��0��6    $��2��60   $��Generate Password��6D   $��0,10,0,5q�6_   $��5q�6q   .��5+      ��  �� Orientation$ 
Horizontal=�6   .+�5,      �� $��Length:��6   $��Center=�6*   �� 5-      -   5        $ LengthComboBox��5-      $��60��63   $��Center=�6>   $	��5,0q�6Y    �� SelectedIndex$ 10��6f   ��5.      �� *PasswordLengths+ 6C   �� *SelectedPasswordLength+�� 5/      $��Generate��6   $	��5,0q�6X   ��6,   �� *GeneratePasswordCommand+6X   �� 50      $
��Copy��6   $	��5,0q�6Y   ��6(   �� *CopyGeneratedPasswordCommand+y�51      �� *CopyButtonVisibility+52      53      �� 55   
   $��5��6   $��0��6   $��N_otes:��6-   $	��Top=�6k   $��0,5,0,0q�6�   	 6?   �� $
 NotesTextBox��6�   �� 56   
   -   5        $ NotesTextBox��56   /   $��5��6   $��1��6   $��5q�6E   $��3q�6P   $��80��6\   $	��150��6k    �� AcceptsReturn$
 True��58       �� TextWrapping$
 Wrap=�6'   " �� VerticalScrollBarVisibility$
 Auto=�6;   ��57      �� *Entry.Notes+$ PropertyChanged=�58   ;   �� 5:   
   $��6��6   $��0��6"   $��2��62   $ 
Horizontal=�6F   $��Right=�6_   $��0,15,0,5q�6{   .+�5;      �� $��_Save��6    �� 	IsDefault$
 True��6E   $��80��6V   $��5q�6d   ��6%   �� *SaveCommand+6d   �� 5<      $��_Cancel��6    �� IsCancel$
 True��6I   $��80��6Y   $��5q�6g   ��6'   �� *CancelCommand+6g   5=      �� 5?   
   $��7��6   $��0��6!   $��2��61   
���  ��$��0,5,0,0q�6u   $
��Wrap=�6�   ��6E   �� *StatusMessage+6�   5A      5B      