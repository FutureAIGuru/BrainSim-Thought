## Global imports
import os
from typing import Union
from abc import abstractmethod
import tkinter as tk
## Import UKS.dll from C# modules
import clr
clr.AddReference("UKS")
from UKS import *
uks = None
try:
    uks = UKS()
except Exception as e:
    print(e)


class ViewBase(object):
    def __init__(self, 
                 title: str, 
                 level: Union[tk.Tk, tk.Toplevel],
                 module_type: str,
                 uks=uks) -> None:
        self.uks = uks
        self.level = level
        self.level.title(title)
        #self.level.transient()
        self.level.iconbitmap(os.path.join(os.getcwd(), "iconsmall.ico"))
        ## Set UI params
        self.module_type = module_type
        self.label = ""
        #for future resize event capture
        self.window_width = None
        self.window_height = None
        self.window_x = None
        self.window_y = None
        # Call bind_resize_events() after build if geometry sync to UKS is needed.
        
    def setLabel(self, new_label: str):
        self.label = new_label

    def bind_resize_events(self):
        self.level.bind("<Configure>", self.resize)

    def resize(self, event):
        # Only handle the toplevel window; child Configure events caused Windows crashes.
        if event.widget is not self.level:
            return
        if (self.window_width != event.width) or (self.window_height != event.height):
            self.window_width, self.window_height = event.width, event.height
        if (self.window_x != event.x) or (self.window_y != event.y):
            self.window_x, self.window_y = event.x, event.y
        # TODO: persist geometry to UKS module attributes when safe

    def close(self):
        self.level.destroy()
        

    


    @abstractmethod
    def build(self):
        ...
    
    @abstractmethod
    def fire(self):
        ...
