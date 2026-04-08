<script setup lang="ts">
import { RouterLink, RouterView } from 'vue-router'
</script>

<template>
  <div class="min-h-screen bg-gray-50 flex flex-col md:flex-row">
    
    <nav class="bg-gray-900 text-white w-full md:w-64 flex-shrink-0 p-4 flex md:flex-col gap-2 shadow-xl z-10">
      <div class="font-black text-2xl mb-6 hidden md:block text-brand tracking-wide">
        Admin Panel
      </div>
      
      <RouterLink 
        to="/" 
        class="px-4 py-3 rounded-lg hover:bg-gray-800 transition-colors flex items-center gap-2 text-gray-300 hover:text-white"
        active-class="bg-brand text-white font-bold shadow-md"
      >
        <span>📊</span> Dashboard
      </RouterLink>
      
      <RouterLink 
        to="/ai-config" 
        class="px-4 py-3 rounded-lg hover:bg-gray-800 transition-colors flex items-center gap-2 text-gray-300 hover:text-white"
        active-class="bg-brand text-white font-bold shadow-md"
      >
        <span>🧠</span> Configurare AI
      </RouterLink>

      <RouterLink 
        to="/rules" 
        class="px-4 py-3 rounded-lg hover:bg-gray-800 transition-colors flex items-center gap-2 text-gray-300 hover:text-white"
        active-class="bg-brand text-white font-bold shadow-md"
      >
        <span>🔗</span> Reguli Recomandări
      </RouterLink>
    </nav>

    <main class="flex-1 p-4 md:p-8 overflow-y-auto">
      <RouterView /> 
    </main>

  </div>
</template>