class kaPanel extends TemplateElement {
    constructor() {
        super(...arguments);

        this.started = false;
		this.isActive = false;
        this.busy = false;
        this.front = true;
        this.url = "";
        this.zoom = 0;
        this.debugEnabled = false;
        this.onStorageReady = () => {
            if (this.debugEnabled) {
                var self = this;
                setTimeout(() => {
                    self.isDebugEnabled();
                }, 1000);
            } else {
                this.initialize();
            }
        };
    }
    isDebugEnabled() {
        var self = this;
        if (typeof g_modDebugMgr != "undefined") {
            g_modDebugMgr.AddConsole(null);
            g_modDebugMgr.AddDebugButton("Clear", function() {
                g_modDebugMgr.ClearConsole();
                console.log('Clear');
            });
            g_modDebugMgr.AddDebugButton("Reload", function() {
                console.log('Reload');
                window.document.location.reload(true);
            });
            g_modDebugMgr.AddDebugButton("Source", function() {
                console.log('Source');
                console.log(window.document.documentElement.outerHTML);
            });
            g_modDebugMgr.AddDebugButton("close", function() {
                console.log('close');
                if (self.ingameUi) {
                    console.log('ingameUi');
                    self.ingameUi.closePanel();
                }
            });
            g_modDebugMgr.AddDebugButton("min", function() {
                console.log('min');
                if (self.ingameUi && self.ingameUi.m_toolbar_listener) {
                    console.log('ingameUi');
                    self.ingameUi.m_toolbar_listener.setMinimized('KA_PANEL', true);
                }
            });
            this.initialize();
        } else {
            Include.addScript("/JS/debug.js", function () {
                if (typeof g_modDebugMgr != "undefined") {
                    g_modDebugMgr.AddConsole(null);
                    g_modDebugMgr.AddDebugButton("Clear", function() {
                        g_modDebugMgr.ClearConsole();
                        console.log('Clear');
                    });
                    g_modDebugMgr.AddDebugButton("Reload", function() {
                        console.log('Reload');
                        window.document.location.reload(true);
                    });
                    g_modDebugMgr.AddDebugButton("Source", function() {
                        console.log('Source');
                        console.log(window.document.documentElement.outerHTML);
                    });
                    g_modDebugMgr.AddDebugButton("close", function() {
                        console.log('close');
                        if (self.ingameUi) {
                            console.log('ingameUi');
                            self.ingameUi.closePanel();
                        }
                    });
                    g_modDebugMgr.AddDebugButton("min", function() {
                        console.log('min');
                        if (self.ingameUi && self.ingameUi.m_toolbar_listener) {
                            console.log('ingameUi');
                            self.ingameUi.m_toolbar_listener.setMinimized('KA_PANEL', true);
                        }
                    });
                    self.initialize();
                } else {
                    setTimeout(() => {
                        self.isDebugEnabled();
                    }, 2000);
                }
            });
        }
    }
    connectedCallback() {
        super.connectedCallback();
        document.addEventListener("dataStorageReady", this.onStorageReady);
    }
    initialize() {
        if (this.started) {
            return;
        }

        var self = this;

        this.url = "http://localhost:8212";

        this.errorElement = document.getElementById("kaPanelImageError");
        this.imageElementFront = document.getElementById("kaPanelImageFront");
        this.imageElementBack = document.getElementById("kaPanelImageBack");
        this.imageElementZoom = document.getElementById("kaPanelImageZoom");
        this.ingameUi = this.querySelector('ingame-ui');


        if (this.imageElementFront && this.imageElementBack) {
            this.imageElementFront.addEventListener("error", () => {
                self.onImageError();
            });
            this.imageElementFront.addEventListener("load", () => {
                self.onImageLoad();
            });
            this.imageElementBack.addEventListener("error", () => {
                self.onImageError();
            });
            this.imageElementBack.addEventListener("load", () => {
                self.onImageLoad();
            });
            if (this.imageElementZoom) {
				this.imageElementZoom.addEventListener('wheel', () => {
				  event.preventDefault();
				  this.zoom  += event.deltaY * 0.01;
				  self.updateImage();
				});
            }
        }

        if (this.ingameUi) {
            this.ingameUi.addEventListener("panelActive", (e) => {
				this.isActive = true;
                self.updateImage();
            });
            this.ingameUi.addEventListener("panelInactive", (e) => {
				this.isActive = false;
            });
            this.ingameUi.addEventListener("onResizeElement", () => {
                self.updateImage();
            });
            this.ingameUi.addEventListener("dblclick", () => {
            });
        }

        this.started = true;
		
		setInterval(function() {
			if (!this.isActive)
				return;
			
			self.updateImage();
		}, 10000);
    }
    disconnectedCallback() {
        document.removeEventListener("dataStorageReady", this.onStorageReady);
        super.disconnectedCallback();
    }
    onImageError() {
        var self = this;

		if (!this.isActive)
			return;
		
        if (this.front) {
            if (this.imageElementFront) {
                this.imageElementFront.setAttribute('style', '');
                this.imageElementFront.setAttribute('class', 'hidden');
            }
            this.front = false;
        } else {
            if (this.imageElementBack) {
                this.imageElementBack.setAttribute('style', '');
                this.imageElementBack.setAttribute('class', 'hidden');
            }
            this.front = true;
        }
        if (this.errorElement) {
            this.errorElement.setAttribute('class', 'show');
        }
        //this.busy = false;
        setTimeout(function() {
            self.updateImage();
        }, 250);
    }
    onImageLoad() {
        var self = this;

		if (!this.isActive)
			return;
		
        if (this.errorElement) {
            this.errorElement.setAttribute('class', '');
        }

		if (this.front) {
            if (this.imageElementFront) {
                this.imageElementFront.setAttribute('class', 'show');
                setTimeout(function(){
					this.imageElementBack.setAttribute('class', 'hidden');
				}, 0);
                //this.imageElementFront.setAttribute('style', 'z-index: 2');
                //if (this.imageElementBack) {
               //     this.imageElementBack.setAttribute('style', '');
                //}
            }
            this.front = false;
        } else {
            if (this.imageElementBack) {
                this.imageElementBack.setAttribute('class', 'show');
                setTimeout(function(){
					this.imageElementFront.setAttribute('class', 'hidden');
				}, 0);
                //this.imageElementBack.setAttribute('style', 'z-index: 2');
                //if (this.imageElementFront) {
                //    this.imageElementFront.setAttribute('style', '');
                //}
            }
            this.front = true;
        }
		
		this.front != this.front;
        //this.busy = false;
        setTimeout(function() {
            self.updateImage();
        }, 250);
    }
    updateImage() {
        if (! this.url) {
            return;
        }
		
		var url = this.url + "/?width=" + Math.max(this.imageElementFront.width, 138) + "&height=" + Math.max(this.imageElementFront.height, 155) +"&zoom="+this.zoom.toString()+"&cmd=" + Math.random();
		this.zoom = 0;
        if (this.front) {
            if (this.imageElementFront) {
                //if (! this.busy) {
                    //this.busy = true;
                    this.imageElementFront.src = url;
					//this.imageElementFront.setAttribute('class', 'hidden');
                //}
            }
        } else {
            if (this.imageElementBack) {
                //if (! this.busy) {
                    //this.busy = true;
                    this.imageElementBack.src = url;
					//this.imageElementBack.setAttribute('class', 'hidden');
                //}
            }
        }
    }
}
window.customElements.define("ingamepanel-ka", kaPanel);
checkAutoload();