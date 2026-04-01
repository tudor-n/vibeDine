import { createRouter, createWebHistory } from 'vue-router'
import OrderingView from '../view/OrderingView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/:qrToken?',
      name: 'ordering',
      component: OrderingView
    }
  ]
})

export default router
