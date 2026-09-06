// ============================================================
// MusicFree 插件运行时引导 (在 Jint 沙箱中执行)
// 提供: console / atob / btoa / setTimeout / URL / URLSearchParams
//       TextEncoder / TextDecoder / require / axios 兼容层
// 宿主桥: __hostHttp(url, optionsJson) -> responseJson (同步阻塞)
//         __log(level, message)
//         __storageGet(key) / __storageSet(key, value) / __storageRemove(key)
// ============================================================
(function () {
    'use strict';

    // ---------- Function.toString 重写(混淆源完整性校验, 与 LX 引导一致) ----------
    // Jint 对所有函数返回 "function () { [native code] }", V8 返回真实源码。
    // jsjiami.v7 等混淆源的完整性 gadget 用正则校验 toString 输出, 失败会进入
    // push+len 的无限 for 循环卡死引擎。fake 必须用 V8 紧凑形态
    // "function NAME(){return'ok';}"(带空格/双引号形态会被正则拒绝, 实测验证)。
    var __fts = Function.prototype.toString;
    Function.prototype.toString = function () {
        try {
            var n = (this && this.name && /^[\w$]*$/.test(String(this.name))) ? String(this.name) : '';
            return "function " + n + "(){return'ok';}";
        } catch (e) { return "function(){return'ok';}"; }
    };

    // ---------- console ----------
    globalThis.console = {
        log: function () { __hostLog('info', __joinArgs(arguments)); },
        info: function () { __hostLog('info', __joinArgs(arguments)); },
        warn: function () { __hostLog('warn', __joinArgs(arguments)); },
        error: function () { __hostLog('error', __joinArgs(arguments)); },
        debug: function () { __hostLog('debug', __joinArgs(arguments)); }
    };
    function __joinArgs(args) {
        var parts = [];
        for (var i = 0; i < args.length; i++) {
            var a = args[i];
            try {
                parts.push(typeof a === 'string' ? a : JSON.stringify(a));
            } catch (e) { parts.push(String(a)); }
        }
        return parts.join(' ');
    }

    // ---------- base64 ----------
    var __b64chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
    function __btoa(input) {
        var output = '';
        var i = 0;
        while (i < input.length) {
            var a = input.charCodeAt(i++) & 0xff;
            var b = i < input.length ? input.charCodeAt(i++) & 0xff : NaN;
            var c = i < input.length ? input.charCodeAt(i++) & 0xff : NaN;
            var b1 = a >> 2;
            var b2 = ((a & 3) << 4) | (isNaN(b) ? 0 : (b >> 4));
            var b3 = isNaN(b) ? 64 : (((b & 15) << 2) | (isNaN(c) ? 0 : (c >> 6)));
            var b4 = isNaN(c) ? 64 : (c & 63);
            output += __b64chars.charAt(b1) + __b64chars.charAt(b2) + __b64chars.charAt(b3) + __b64chars.charAt(b4);
        }
        return output;
    }
    function __atob(input) {
        input = String(input).replace(/[^A-Za-z0-9+/=]/g, '');
        var output = '';
        var i = 0;
        while (i < input.length) {
            var e1 = __b64chars.indexOf(input.charAt(i++));
            var e2 = __b64chars.indexOf(input.charAt(i++));
            var e3 = __b64chars.indexOf(input.charAt(i++));
            var e4 = __b64chars.indexOf(input.charAt(i++));
            var c1 = (e1 << 2) | (e2 >> 4);
            var c2 = ((e2 & 15) << 4) | (e3 >> 2);
            var c3 = ((e3 & 3) << 6) | e4;
            if (e3 === 64) output += String.fromCharCode(c1);
            else if (e4 === 64) output += String.fromCharCode(c1, c2);
            else output += String.fromCharCode(c1, c2, c3);
        }
        return output;
    }
    globalThis.btoa = __btoa;
    globalThis.atob = __atob;

    function __bytesToBase64(bytes) {
        var bin = '';
        for (var i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
        return __btoa(bin);
    }
    function __base64ToBytes(b64) {
        var bin = __atob(b64);
        var bytes = new Uint8Array(bin.length);
        for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
        return bytes;
    }

    // ---------- utf8 ----------
    function __utf8Encode(str) {
        str = String(str);
        var out = [];
        for (var i = 0; i < str.length; i++) {
            var code = str.codePointAt(i);
            if (code > 0xffff) i++;
            if (code < 0x80) out.push(code);
            else if (code < 0x800) out.push(0xc0 | (code >> 6), 0x80 | (code & 63));
            else if (code < 0x10000) out.push(0xe0 | (code >> 12), 0x80 | ((code >> 6) & 63), 0x80 | (code & 63));
            else out.push(0xf0 | (code >> 18), 0x80 | ((code >> 12) & 63), 0x80 | ((code >> 6) & 63), 0x80 | (code & 63));
        }
        return new Uint8Array(out);
    }
    function __utf8Decode(bytes) {
        var out = '';
        var i = 0;
        while (i < bytes.length) {
            var b = bytes[i];
            if (b < 0x80) { out += String.fromCharCode(b); i += 1; }
            else if (b < 0xe0) { out += String.fromCharCode(((b & 31) << 6) | (bytes[i + 1] & 63)); i += 2; }
            else if (b < 0xf0) {
                var cp = ((b & 15) << 12) | ((bytes[i + 1] & 63) << 6) | (bytes[i + 2] & 63);
                out += String.fromCharCode(cp); i += 3;
            } else {
                var cp4 = ((b & 7) << 18) | ((bytes[i + 1] & 63) << 12) | ((bytes[i + 2] & 63) << 6) | (bytes[i + 3] & 63);
                out += String.fromCodePoint(cp4); i += 4;
            }
        }
        return out;
    }
    globalThis.TextEncoder = function () { };
    globalThis.TextEncoder.prototype.encode = function (s) { return __utf8Encode(s); };
    // TextDecoder: utf-8 走 JS 解码, gb18030/gbk 等桥接原生 __hostDecodeText(对齐弦予, 酷狗/酷我接口需要)
    globalThis.TextDecoder = function (label) {
        this.__label = String(label || 'utf-8').toLowerCase();
    };
    globalThis.TextDecoder.prototype.decode = function (b) {
        var bytes = b instanceof Uint8Array ? b : new Uint8Array(b);
        if (this.__label === 'utf-8' || this.__label === 'utf8') return __utf8Decode(bytes);
        if (typeof __hostDecodeText === 'function') {
            try { return __hostDecodeText(__bytesToBase64(bytes), this.__label); } catch (e) { /* 回退 utf-8 */ }
        }
        return __utf8Decode(bytes);
    };

    // ---------- setTimeout (微任务实现, 延迟被近似为0) ----------
    var __timerCount = 0;
    globalThis.setTimeout = function (fn, delay) {
        if (__timerCount > 200) return 0; // 防失控
        __timerCount++;
        if (typeof fn === 'function') {
            Promise.resolve().then(function () { __timerCount--; try { fn(); } catch (e) { console.error('setTimeout callback error: ' + (e && e.message)); } });
        }
        return __timerCount;
    };
    globalThis.clearTimeout = function () { };
    // setInterval: 仅注册不执行(与 LX 引导一致)。同步执行会触发混淆插件(jsjiami.v7 等)
    // 注册在 setInterval 上的反调试死循环(while(!![]){}), 卡死引擎直至语句数超限。
    globalThis.setInterval = function () { return 0; };
    globalThis.clearInterval = function () { };

    // ---------- queueMicrotask / performance / navigator / crypto (对齐弦予 host_shim) ----------
    globalThis.queueMicrotask = function (fn) { Promise.resolve().then(fn); };
    globalThis.performance = globalThis.performance || {
        now: function () { return Date.now(); },
        timeOrigin: Date.now()
    };
    globalThis.navigator = globalThis.navigator || {
        userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36',
        platform: 'Win32',
        language: 'zh-CN',
        languages: ['zh-CN', 'zh'],
        onLine: true
    };
    globalThis.crypto = globalThis.crypto || {};
    if (!globalThis.crypto.getRandomValues) {
        globalThis.crypto.getRandomValues = function (arr) {
            var n = arr.length;
            if (n <= 0 || n > 65536) throw new RangeError('crypto.getRandomValues length out of range');
            var b64 = typeof __hostRandomBytes === 'function' ? __hostRandomBytes(n) : null;
            if (b64) {
                var bytes = __base64ToBytes(b64);
                for (var i = 0; i < n; i++) arr[i] = bytes[i];
            } else {
                for (var i2 = 0; i2 < n; i2++) arr[i2] = Math.floor(Math.random() * 256);
            }
            return arr;
        };
    }
    if (!globalThis.crypto.randomUUID) {
        globalThis.crypto.randomUUID = function () {
            var b = new Uint8Array(16);
            globalThis.crypto.getRandomValues(b);
            b[6] = (b[6] & 15) | 64; b[8] = (b[8] & 63) | 128;
            var hex = [];
            for (var i = 0; i < 16; i++) hex.push((b[i] + 0x100).toString(16).slice(1));
            return hex.slice(0, 4).join('') + '-' + hex.slice(4, 6).join('') + '-' + hex.slice(6, 8).join('') + '-' + hex.slice(8, 10).join('') + '-' + hex.slice(10, 16).join('');
        };
    }

    // ---------- URLSearchParams (最小实现) ----------
    function URLSearchParams(init) {
        this._pairs = [];
        if (typeof init === 'string') {
            if (init.charAt(0) === '?') init = init.slice(1);
            var parts = init.split('&');
            for (var i = 0; i < parts.length; i++) {
                if (!parts[i]) continue;
                var idx = parts[i].indexOf('=');
                var k = idx < 0 ? parts[i] : parts[i].slice(0, idx);
                var v = idx < 0 ? '' : parts[i].slice(idx + 1);
                this._pairs.push([__urlDecode(k), __urlDecode(v)]);
            }
        } else if (init && typeof init === 'object' && !(init instanceof URLSearchParams)) {
            for (var key in init) {
                if (Object.prototype.hasOwnProperty.call(init, key)) this._pairs.push([String(key), String(init[key])]);
            }
        }
    }
    URLSearchParams.prototype.append = function (k, v) { this._pairs.push([String(k), String(v)]); };
    URLSearchParams.prototype.get = function (k) {
        for (var i = 0; i < this._pairs.length; i++) if (this._pairs[i][0] === String(k)) return this._pairs[i][1];
        return null;
    };
    URLSearchParams.prototype.getAll = function (k) {
        var r = [];
        for (var i = 0; i < this._pairs.length; i++) if (this._pairs[i][0] === String(k)) r.push(this._pairs[i][1]);
        return r;
    };
    URLSearchParams.prototype.has = function (k) { return this.get(k) !== null; };
    URLSearchParams.prototype.set = function (k, v) {
        var found = false;
        for (var i = 0; i < this._pairs.length; i++) {
            if (this._pairs[i][0] === String(k)) {
                if (found) { this._pairs.splice(i, 1); i--; }
                else { this._pairs[i][1] = String(v); found = true; }
            }
        }
        if (!found) this._pairs.push([String(k), String(v)]);
    };
    URLSearchParams.prototype['delete'] = function (k) {
        for (var i = 0; i < this._pairs.length; i++) {
            if (this._pairs[i][0] === String(k)) { this._pairs.splice(i, 1); i--; }
        }
    };
    URLSearchParams.prototype.toString = function () {
        var parts = [];
        for (var i = 0; i < this._pairs.length; i++) {
            parts.push(__urlEncode(this._pairs[i][0]) + '=' + __urlEncode(this._pairs[i][1]));
        }
        return parts.join('&');
    };
    URLSearchParams.prototype.forEach = function (cb) {
        for (var i = 0; i < this._pairs.length; i++) cb(this._pairs[i][1], this._pairs[i][0]);
    };
    globalThis.URLSearchParams = URLSearchParams;

    // ---------- URL (最小实现) ----------
    function URL(url, base) {
        url = String(url);
        if (base) {
            base = String(base);
            if (!/^[a-z][a-z0-9+.-]*:/i.test(url)) {
                var bMatch = /^(https?:)\/\/([^/?#]+)([^?#]*)/i.exec(base);
                if (bMatch) {
                    var path = bMatch[3] || '/';
                    if (url.charAt(0) === '/') path = url;
                    else {
                        var dir = path.slice(0, path.lastIndexOf('/') + 1);
                        path = dir + url;
                    }
                    url = bMatch[1] + '//' + bMatch[2] + path;
                }
            }
        }
        var m = /^([a-z][a-z0-9+.-]*:)?(?:\/\/([^/?#@]*)(?:@([^/?#]*))?)?([^?#]*)(\?[^#]*)?(#.*)?$/i.exec(url);
        if (!m) throw new Error('Invalid URL: ' + url);
        this.protocol = m[1] || '';
        var hostauth = m[3] !== undefined && m[3] !== null ? m[3] : m[2] || '';
        var at = hostauth.lastIndexOf('@');
        var hostpart = at >= 0 ? hostauth.slice(at + 1) : hostauth;
        var pm = /^([^:]*)(?::(\d+))?$/.exec(hostpart);
        this.hostname = pm ? pm[1] : '';
        this.port = pm && pm[2] ? pm[2] : '';
        this.host = this.port ? this.hostname + ':' + this.port : this.hostname;
        this.origin = this.protocol + '//' + this.host;
        this.pathname = m[4] || (this.host ? '/' : '');
        this.search = m[5] || '';
        this.hash = m[6] || '';
        this.searchParams = new URLSearchParams(this.search.slice(1));
        this._href = url;
    }
    URL.prototype.toString = function () { return this._href; };
    Object.defineProperty(URL.prototype, 'href', {
        get: function () { return this._href; },
        set: function (v) { var u = new URL(String(v)); for (var k in u) if (k !== '_href') this[k] = u[k]; this._href = u._href; }
    });
    URL.createObjectURL = function () { throw new Error('URL.createObjectURL is not supported'); };
    globalThis.URL = URL;

    function __urlEncode(s) {
        return encodeURIComponent(String(s)).replace(/[!'()*]/g, function (c) {
            return '%' + c.charCodeAt(0).toString(16).toUpperCase();
        });
    }
    function __urlDecode(s) {
        try { return decodeURIComponent(String(s).replace(/\+/g, ' ')); } catch (e) { return String(s); }
    }

    // ---------- 模块系统 ----------
    var __modules = {};
    globalThis.__defineModule = function (name, factory) {
        var module = { exports: {} };
        factory(module, module.exports);
        // bakamusic 兼容: 为对象/函数模块补 default 指向自身
        // (axios 垫片导出的是函数, typeof 'function' 不属于 'object', 也需补 default,
        //  否则插件侧 axios_1.default.get 报 "property default 没有定义")
        var value = module.exports;
        if (value && (typeof value === 'object' || typeof value === 'function') && !('default' in value)) {
            try { Object.defineProperty(value, 'default', { configurable: true, enumerable: false, value: value }); } catch (e) { }
        }
        __modules[name] = value;
    };

    var __unsupportedModules = {};
    globalThis.__markUnsupported = function (name, reason) { __unsupportedModules[name] = reason; };

    globalThis.require = function (name) {
        if (typeof name !== 'string') return null;
        // 归一化: axios/lib/xxx → axios
        var norm = name.split('/')[0];
        if (name.indexOf('@') === 0) {
            // @scope/pkg(/sub)?
            var segs = name.split('/');
            norm = segs.length >= 2 ? segs[0] + '/' + segs[1] : name;
        }
        if (__modules[norm] !== undefined) return __modules[norm];
        if (__modules[name] !== undefined) return __modules[name];
        if (__unsupportedModules[norm] || __unsupportedModules[name]) {
            throw new Error('Module "' + name + '" is not supported: ' + (__unsupportedModules[norm] || __unsupportedModules[name]));
        }
        return null;
    };

    // ---------- axios 兼容层 ----------
    function __mergeHeaders(target, src) {
        if (!src) return;
        for (var k in src) {
            if (Object.prototype.hasOwnProperty.call(src, k)) {
                var lk = k.toLowerCase();
                var replaced = false;
                for (var tk in target) {
                    if (Object.prototype.hasOwnProperty.call(target, tk) && tk.toLowerCase() === lk) {
                        target[tk] = src[k]; replaced = true; break;
                    }
                }
                if (!replaced) target[k] = src[k];
            }
        }
    }
    function __isPlainObject(v) {
        return v && typeof v === 'object' && !(v instanceof Uint8Array) && !(v instanceof ArrayBuffer);
    }
    function __serializeParams(params) {
        if (!params) return '';
        if (typeof params === 'string') return params;
        if (params instanceof URLSearchParams) return params.toString();
        var parts = [];
        for (var k in params) {
            if (!Object.prototype.hasOwnProperty.call(params, k)) continue;
            var v = params[k];
            if (Array.isArray(v)) {
                for (var i = 0; i < v.length; i++) parts.push(__urlEncode(k) + '=' + __urlEncode(v[i]));
            } else if (v !== undefined && v !== null) {
                parts.push(__urlEncode(k) + '=' + __urlEncode(v));
            }
        }
        return parts.join('&');
    }
    function AxiosError(message, code, config, response) {
        this.name = 'AxiosError';
        this.message = message || '';
        this.code = code;
        this.config = config;
        this.response = response;
        this.isAxiosError = true;
        if (response) { this.status = response.status; }
        this.stack = (new Error(message)).stack;
    }
    AxiosError.prototype = Object.create(Error.prototype);
    AxiosError.prototype.constructor = AxiosError;

    function __createAxios(defaults) {
        var axDefaults = defaults || {};
        if (!axDefaults.headers) axDefaults.headers = {};
        if (!axDefaults.headers.common) axDefaults.headers.common = {};
        if (!axDefaults.headers.get) axDefaults.headers.get = {};
        if (!axDefaults.headers.post) axDefaults.headers.post = {};

        function axiosImpl(config) { return __dispatch(config, axDefaults, axiosImpl.interceptors); }
        axiosImpl.defaults = axDefaults;
        axiosImpl.interceptors = {
            request: __makeInterceptorManager(),
            response: __makeInterceptorManager()
        };
        axiosImpl.request = function (config) { return __dispatch(config, axDefaults, axiosImpl.interceptors); };
        axiosImpl.get = function (url, config) {
            config = Object.assign({}, config || {}); config.method = 'get'; config.url = url;
            return __dispatch(config, axDefaults, axiosImpl.interceptors);
        };
        axiosImpl['delete'] = function (url, config) {
            config = Object.assign({}, config || {}); config.method = 'delete'; config.url = url;
            return __dispatch(config, axDefaults, axiosImpl.interceptors);
        };
        axiosImpl.head = function (url, config) {
            config = Object.assign({}, config || {}); config.method = 'head'; config.url = url;
            return __dispatch(config, axDefaults, axiosImpl.interceptors);
        };
        axiosImpl.options = function (url, config) {
            config = Object.assign({}, config || {}); config.method = 'options'; config.url = url;
            return __dispatch(config, axDefaults, axiosImpl.interceptors);
        };
        axiosImpl.post = function (url, data, config) {
            config = Object.assign({}, config || {}); config.method = 'post'; config.url = url; config.data = data;
            return __dispatch(config, axDefaults, axiosImpl.interceptors);
        };
        axiosImpl.put = function (url, data, config) {
            config = Object.assign({}, config || {}); config.method = 'put'; config.url = url; config.data = data;
            return __dispatch(config, axDefaults, axiosImpl.interceptors);
        };
        axiosImpl.patch = function (url, data, config) {
            config = Object.assign({}, config || {}); config.method = 'patch'; config.url = url; config.data = data;
            return __dispatch(config, axDefaults, axiosImpl.interceptors);
        };
        axiosImpl.getUri = function (config) {
            var merged = __normalizeConfig(config || {}, axDefaults);
            return merged.url;
        };
        axiosImpl.create = function (config) {
            var nd = JSON.parse(JSON.stringify(axDefaults));
            if (config) {
                for (var k in config) {
                    if (k === 'headers') { __mergeHeaders(nd.headers.common, config.headers); }
                    else nd[k] = config[k];
                }
            }
            return __createAxios(nd);
        };
        axiosImpl.AxiosError = AxiosError;
        axiosImpl.CanceledError = AxiosError;
        axiosImpl.isAxiosError = function (e) { return !!(e && e.isAxiosError); };
        axiosImpl.isCancel = function (e) { return !!(e && (e.code === 'ERR_CANCELED' || e.__CANCEL__)); };
        axiosImpl.all = function (promises) { return Promise.all(promises); };
        axiosImpl.spread = function (cb) { return function (arr) { return cb.apply(null, arr); }; };
        return axiosImpl;
    }

    function __makeInterceptorManager() {
        var handlers = [];
        return {
            use: function (fulfilled, rejected) { handlers.push({ fulfilled: fulfilled, rejected: rejected }); return handlers.length - 1; },
            eject: function (id) { if (handlers[id]) handlers[id] = null; },
            __apply: function (value, isError) {
                for (var i = 0; i < handlers.length; i++) {
                    var h = handlers[i];
                    if (!h) continue;
                    try {
                        if (isError) { if (h.rejected) value = h.rejected(value); }
                        else if (h.fulfilled) value = h.fulfilled(value);
                    } catch (e) {
                        return { __thrown: e };
                    }
                }
                return { value: value };
            }
        };
    }

    function __normalizeConfig(config, defaults) {
        var method = String(config.method || 'get').toLowerCase();
        var url = config.url || '';
        if (defaults.baseURL && url && !/^[a-z][a-z0-9+.-]*:/i.test(url)) {
            url = String(defaults.baseURL).replace(/\/+$/, '') + '/' + url.replace(/^\/+/, '');
        }
        var headers = {};
        __mergeHeaders(headers, defaults.headers.common);
        __mergeHeaders(headers, defaults.headers[method]);
        __mergeHeaders(headers, config.headers);
        var qs = __serializeParams(config.params);
        if (qs) url += (url.indexOf('?') >= 0 ? '&' : '?') + qs;

        var body = config.data;
        if (body === undefined || body === null) body = null;
        else if (typeof body === 'string') { /* 原样 */ }
        else if (body instanceof Uint8Array || body instanceof ArrayBuffer) { /* 二进制原样 */ }
        else if (__isPlainObject(body)) {
            var ct = '';
            for (var hk in headers) { if (hk.toLowerCase() === 'content-type') ct = String(headers[hk]); }
            if (/x-www-form-urlencoded/i.test(ct)) {
                body = __serializeParams(body);
            } else {
                if (!ct) headers['Content-Type'] = 'application/json';
                body = JSON.stringify(body);
            }
        } else { body = String(body); }

        return {
            url: url,
            method: method,
            headers: headers,
            body: body,
            timeout: (typeof config.timeout === 'number' && config.timeout > 0) ? config.timeout :
                     (typeof defaults.timeout === 'number' && defaults.timeout > 0) ? defaults.timeout : 15000,
            responseType: config.responseType || 'json',
            validateStatus: config.validateStatus,
            withCredentials: config.withCredentials,
            config: config
        };
    }

    function __dispatch(config, defaults, interceptors) {
        var merged = __normalizeConfig(config, defaults);
        var reqInterceptors = (interceptors && interceptors.request) || null;
        if (reqInterceptors) {
            var wrapped = { url: merged.url, method: merged.method, headers: merged.headers, data: config.data, params: config.params, timeout: merged.timeout, responseType: merged.responseType, baseURL: defaults.baseURL };
            var res = reqInterceptors.__apply(wrapped, false);
            if (res.__thrown) throw res.__thrown;
            merged = __normalizeConfig(res.value, defaults);
        }
        try {
            var response = __performRequest(merged);
            if (interceptors && interceptors.response) {
                var out = interceptors.response.__apply(response, false);
                if (out.__thrown) throw out.__thrown;
                return out.value;
            }
            return response;
        } catch (e) {
            if (interceptors && interceptors.response) {
                var outErr = interceptors.response.__apply(e, true);
                if (outErr.__thrown) throw outErr.__thrown;
                if (outErr.value !== e) return outErr.value;
            }
            throw e;
        }
    }

    function __performRequest(merged) {
        var options = {
            method: merged.method,
            headers: merged.headers,
            timeoutMs: merged.timeout
        };
        if (merged.body !== null && merged.body !== undefined) {
            if (merged.body instanceof Uint8Array || merged.body instanceof ArrayBuffer) {
                var u8 = merged.body instanceof Uint8Array ? merged.body : new Uint8Array(merged.body);
                options.bodyBase64 = __bytesToBase64(u8);
            } else {
                options.body = String(merged.body);
            }
        }
        var raw = __hostHttp(merged.url, JSON.stringify(options));
        var parsed = JSON.parse(raw);
        if (parsed.error) {
            throw new AxiosError(parsed.error, parsed.code || 'ECONNREFUSED', merged, null);
        }
        var data;
        var isBinary = merged.responseType === 'arraybuffer' || merged.responseType === 'blob';
        if (isBinary) {
            data = parsed.bodyBase64 ? __base64ToBytes(parsed.bodyBase64) : new Uint8Array(0);
        } else {
            var text = parsed.body || '';
            if (merged.responseType === 'json' || merged.responseType === undefined) {
                try { data = JSON.parse(text); } catch (e) { data = text; }
            } else {
                data = text;
            }
        }
        var response = {
            data: data,
            status: parsed.statusCode,
            statusText: parsed.statusText || '',
            headers: parsed.headers || {},
            config: merged.config || merged,
            request: {}
        };
        var validate = merged.validateStatus;
        var ok = validate ? validate(response.status) : (response.status >= 200 && response.status < 300);
        if (!ok) {
            throw new AxiosError('Request failed with status code ' + response.status, 'ERR_BAD_RESPONSE', merged, response);
        }
        return response;
    }

    __defineModule('axios', function (module) { module.exports = __createAxios({}); });
    __defineModule('axios/index.js', function (module) { module.exports = __modules['axios']; });

    // ---------- Buffer (最小实现) ----------
    // 关键: Node Buffer 实例支持 toString(encoding, start, end)。
    // Jint 里裸 Uint8Array.toString() 输出逗号分隔数字串, 插件签名算法
    // (如 QQ zzcSign 的 Buffer.from(bytes).toString('base64'))会因此生成
    // 非法签名, 服务端返回 code:500001, 插件再取 res.req_1.data 就报
    // "Cannot read properties of undefined (reading 'data')"。
    function __bufferToString(u8, encoding, start, end) {
        var enc = String(encoding || 'utf8').toLowerCase();
        var s = start === undefined || start === null ? 0 : (start >>> 0);
        var e = end === undefined || end === null ? u8.length : (end >>> 0);
        if (s < 0) s = 0;
        if (e > u8.length) e = u8.length;
        var view = s < e ? u8.subarray(s, e) : new Uint8Array(0);
        if (enc === 'base64') return __bytesToBase64(view);
        if (enc === 'hex') {
            var h = '';
            for (var i = 0; i < view.length; i++) h += (view[i] < 16 ? '0' : '') + view[i].toString(16);
            return h;
        }
        if (enc === 'binary' || enc === 'latin1' || enc === 'ascii') {
            var b = '';
            for (var j = 0; j < view.length; j++) b += String.fromCharCode(view[j]);
            return b;
        }
        if (enc === 'utf16le' || enc === 'ucs2' || enc === 'ucs-2') {
            var u = '';
            for (var k = 0; k + 1 < view.length; k += 2) u += String.fromCharCode(view[k] | (view[k + 1] << 8));
            return u;
        }
        return __utf8Decode(view); // utf8 / utf-8 及未知编码回退
    }
    function __wrapBuffer(u8) {
        try {
            Object.defineProperty(u8, 'toString', {
                configurable: true, enumerable: false, writable: true,
                value: function (encoding, start, end) { return __bufferToString(u8, encoding, start, end); }
            });
        } catch (e) { }
        return u8;
    }
    __defineModule('buffer', function (module) {
        function Buffer() { }
        Buffer.from = function (input, encoding) {
            if (input instanceof Uint8Array) return __wrapBuffer(input);
            if (input instanceof ArrayBuffer) return __wrapBuffer(new Uint8Array(input));
            if (typeof input === 'string') {
                var enc = String(encoding || 'utf8').toLowerCase();
                if (enc === 'base64') return __wrapBuffer(__base64ToBytes(input));
                if (enc === 'hex') {
                    var out = new Uint8Array(Math.floor(input.length / 2));
                    for (var i = 0; i < out.length; i++) out[i] = parseInt(input.substr(i * 2, 2), 16);
                    return __wrapBuffer(out);
                }
                if (enc === 'binary' || enc === 'latin1' || enc === 'ascii') {
                    var b = new Uint8Array(input.length);
                    for (var j = 0; j < input.length; j++) b[j] = input.charCodeAt(j) & 0xff;
                    return __wrapBuffer(b);
                }
                if (enc === 'utf16le' || enc === 'ucs2' || enc === 'ucs-2') {
                    var u16 = new Uint8Array(input.length * 2);
                    for (var k = 0; k < input.length; k++) {
                        var c = input.charCodeAt(k);
                        u16[k * 2] = c & 0xff; u16[k * 2 + 1] = (c >> 8) & 0xff;
                    }
                    return __wrapBuffer(u16);
                }
                return __wrapBuffer(__utf8Encode(input));
            }
            if (input && typeof input === 'object' && typeof input.length === 'number') {
                var arr = new Uint8Array(input.length);
                for (var m = 0; m < input.length; m++) arr[m] = input[m] & 0xff;
                return __wrapBuffer(arr);
            }
            throw new Error('Buffer.from: unsupported input');
        };
        Buffer.isBuffer = function (v) { return v instanceof Uint8Array; };
        Buffer.alloc = function (size) { return __wrapBuffer(new Uint8Array(size)); };
        Buffer.allocUnsafe = Buffer.alloc;
        Buffer.concat = function (list) {
            var total = 0;
            for (var i = 0; i < list.length; i++) total += list[i].length;
            var out = new Uint8Array(total);
            var off = 0;
            for (var j = 0; j < list.length; j++) { out.set(list[j], off); off += list[j].length; }
            return __wrapBuffer(out);
        };
        module.exports = Buffer;
        module.exports.Buffer = Buffer;
    });

    // ---------- musicfree/storage ----------
    __defineModule('musicfree/storage', function (module) {
        module.exports = {
            getItem: function (key) { return __storageGet(String(key)); },
            setItem: function (key, value) { __storageSet(String(key), value === undefined || value === null ? '' : String(value)); },
            removeItem: function (key) { __storageRemove(String(key)); }
        };
    });

    // ---------- @react-native-cookies/cookies (基于宿主 CookieJar, 对齐弦予 cookie 桥) ----------
    __defineModule('@react-native-cookies/cookies', function (module) {
        function hostOf(url) {
            try { return new URL(url).hostname; } catch (e) { return ''; }
        }
        module.exports = {
            get: function (url) {
                if (typeof __hostCookiesGet === 'function') {
                    try { return __hostCookiesGet(hostOf(String(url))); } catch (e) { return {}; }
                }
                return {};
            },
            set: function (url, name, value) {
                if (typeof __hostCookiesSet === 'function') {
                    try { __hostCookiesSet(hostOf(String(url)), String(name), String(value)); } catch (e) { /* ignore */ }
                }
                return true;
            },
            clearAll: function () {
                if (typeof __hostCookiesClear === 'function') {
                    try { __hostCookiesClear(); } catch (e) { /* ignore */ }
                }
                return true;
            },
            flush: function () { return true; }
        };
    });

    // ---------- 插件调用辅助 ----------
    globalThis.__pluginInstance = null;
    globalThis.__callPluginMethod = function (methodName, argsJson) {
        var args;
        // 宿主可能传入 JSON 字符串或已反序列化的数组字面量, 两者均兼容
        if (typeof argsJson === 'string') {
            try { args = JSON.parse(argsJson); } catch (e) { args = []; }
        } else if (argsJson && typeof argsJson === 'object') {
            args = argsJson;
        } else {
            args = [];
        }
        var state = { done: false, json: null, error: null };
        globalThis.__lastCallResult = state;
        try {
            var fn = globalThis.__pluginInstance ? globalThis.__pluginInstance[methodName] : null;
            if (typeof fn !== 'function') { state.done = true; state.unsupported = true; return state; }
            Promise.resolve(fn.apply(globalThis.__pluginInstance, args)).then(
                function (r) {
                    state.done = true;
                    try { state.json = r === undefined || r === null ? null : JSON.stringify(r); }
                    catch (e) { state.error = 'result not serializable: ' + (e && e.message); }
                },
                function (e) {
                    state.done = true;
                    state.error = (e && e.message) ? String(e.message) : String(e);
                }
            );
        } catch (e) {
            state.done = true;
            state.error = (e && e.message) ? String(e.message) : String(e);
        }
        return state;
    };
})();
