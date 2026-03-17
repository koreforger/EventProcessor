import { createApp } from 'vue'
import App from './App.vue'
import router from './router'

import 'devextreme/dist/css/dx.dark.css'
import './style.css'

createApp(App)
  .use(router)
  .mount('#app')
